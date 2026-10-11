using System.Globalization;
using System.Net;
using System.Text.Json;
using FhirMcp.Core.Mapping;
using FhirMcp.Core.Models;

namespace FhirMcp.Core.Fhir;

/// <summary>Reads a patient's clinical record: medications, conditions, encounters, observations, allergies.</summary>
public sealed partial class FhirApiClient
{
    public const int MaxListResults = 100;

    /// <summary>How many observations to scan when keeping only the latest of each test.</summary>
    internal const int LatestScanSize = 200;

    /// <summary>Observation categories defined by FHIR R4.</summary>
    public static readonly IReadOnlySet<string> ObservationCategories = new HashSet<string>(StringComparer.Ordinal)
    {
        "laboratory", "vital-signs", "social-history", "survey", "exam", "imaging", "procedure", "therapy", "activity",
    };

    /// <summary>Reads one patient by id.</summary>
    public async Task<PatientSummary> GetPatientAsync(string patientId, CancellationToken cancellationToken = default)
    {
        ValidatePatientId(patientId);

        try
        {
            using var doc = await GetJsonAsync("Patient/" + patientId, cancellationToken);
            if (FhirJson.GetString(doc.RootElement, "resourceType") != "Patient")
            {
                throw new FhirRequestException("The FHIR server did not return a Patient.");
            }
            return PatientMapper.Map(doc.RootElement, Today());
        }
        catch (FhirRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            throw new FhirRequestException(
                $"No patient with id \"{patientId}\". Use an id returned by a patient search.", ex.StatusCode, ex);
        }
    }

    /// <summary>Medication orders, newest first. Drugs given by reference are resolved to their names in the same request.</summary>
    public Task<ClinicalList<MedicationSummary>> GetMedicationsAsync(
        string patientId, bool activeOnly = false, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        ValidatePatientId(patientId);
        ValidateMax(maxResults);

        var query = new List<string> { "patient=" + patientId };
        if (activeOnly) query.Add("status=active");
        query.Add("_sort=-authoredon");
        query.Add("_include=MedicationRequest:medication");
        query.Add("_count=" + maxResults);

        return SearchAsync("MedicationRequest", query, ClinicalMapper.MapMedication, maxResults, cancellationToken);
    }

    /// <summary>Problems and diagnoses, most recent onset first. "Active" includes recurrences and relapses.</summary>
    public async Task<ClinicalList<ConditionSummary>> GetConditionsAsync(
        string patientId, bool activeOnly = false, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        ValidatePatientId(patientId);
        ValidateMax(maxResults);

        var query = new List<string> { "patient=" + patientId };
        if (activeOnly) query.Add("clinical-status=active,recurrence,relapse");
        query.Add("_sort=-onset-date");
        query.Add("_count=" + maxResults);

        var result = await SearchAsync("Condition", query, (c, _) => ClinicalMapper.MapCondition(c), maxResults, cancellationToken);

        // Not every server honors the sort; order the page here too so newest is always first.
        return result with { Items = result.Items.OrderByDescending(c => c.Onset ?? DateOnly.MinValue).ToList() };
    }

    /// <summary>Visits and stays, newest first.</summary>
    public async Task<ClinicalList<EncounterSummary>> GetEncountersAsync(
        string patientId, DateOnly? since = null, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        ValidatePatientId(patientId);
        ValidateMax(maxResults);

        var query = new List<string> { "patient=" + patientId };
        if (since is { } s) query.Add("date=ge" + s.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        query.Add("_sort=-date");
        query.Add("_count=" + maxResults);

        var result = await SearchAsync("Encounter", query, (e, _) => ClinicalMapper.MapEncounter(e), maxResults, cancellationToken);
        return result with { Items = result.Items.OrderByDescending(e => e.Start ?? DateOnly.MinValue).ToList() };
    }

    /// <summary>
    /// Lab results, vital signs and other observations, newest first.
    /// With <see cref="ObservationQuery.LatestOnly"/>, keeps only the most recent result of each test,
    /// which answers "what are the latest labs?" without returning years of repeats.
    /// </summary>
    public async Task<ClinicalList<ObservationSummary>> GetObservationsAsync(
        ObservationQuery request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePatientId(request.PatientId);
        ValidateMax(request.MaxResults);

        if (request.Category is { } category && !ObservationCategories.Contains(category))
        {
            throw new ArgumentException(
                $"Unknown category \"{category}\". Use one of: {string.Join(", ", ObservationCategories.Order())}.");
        }

        var baseQuery = new List<string> { "patient=" + request.PatientId };
        if (request.Category is { } c) baseQuery.Add("category=" + c);
        if (!string.IsNullOrWhiteSpace(request.Code)) baseQuery.Add("code=" + Uri.EscapeDataString(request.Code.Trim()));
        if (request.Since is { } s) baseQuery.Add("date=ge" + s.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var fetch = request.LatestOnly ? LatestScanSize : request.MaxResults;
        var text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim();

        var query = new List<string>(baseQuery);
        if (text is not null) query.Add("code:text=" + Uri.EscapeDataString(text));
        query.Add("_sort=-date");
        query.Add("_count=" + fetch);

        var result = await SearchAsync("Observation", query, (o, _) => ClinicalMapper.MapObservation(o), fetch, cancellationToken);

        if (text is not null && result.Items.Count == 0)
        {
            // FHIR's code:text matches only the start of a test's full name, so "a1c" misses
            // "Hemoglobin A1c/Hemoglobin.total in Blood". Scan recent results and match words anywhere.
            result = await ScanObservationsByNameAsync(baseQuery, text, cancellationToken);
        }

        return Shape(result, request);
    }

    /// <summary>How many pages of observations the name fallback scans, at <see cref="LatestScanSize"/> per page.</summary>
    internal const int NameScanPages = 5;

    private async Task<ClinicalList<ObservationSummary>> ScanObservationsByNameAsync(
        List<string> baseQuery, string text, CancellationToken cancellationToken)
    {
        var words = NameMatcherWords(text);
        var query = new List<string>(baseQuery) { "_sort=-date", "_count=" + LatestScanSize };

        var matches = new List<ObservationSummary>();
        var scanned = 0;
        var url = "Observation?" + string.Join('&', query);
        var more = false;

        for (var page = 0; page < NameScanPages && url is not null; page++)
        {
            using var bundle = await GetJsonAsync(url, cancellationToken);
            var root = bundle.RootElement;

            foreach (var resource in ReadEntries(root, "Observation"))
            {
                scanned++;
                var summary = ClinicalMapper.MapObservation(resource);

                // Search the test's own name and its parts' names, so "systolic" finds the
                // Blood Pressure reading that holds a "Systolic Blood Pressure" component.
                var names = FhirJson.GetArray(resource, "component")
                    .Select(c => FhirJson.ConceptText(FhirJson.GetObject(c, "code")))
                    .Append(summary.Name);
                var nameWords = names.Where(n => n is not null).SelectMany(n => NameMatcherWords(n!)).ToList();

                if (words.All(w => nameWords.Any(n => n.StartsWith(w, StringComparison.Ordinal))))
                {
                    matches.Add(summary);
                }
            }

            url = NextPageUrl(root);
            more = url is not null;
        }

        var note = $"No test name started with \"{text}\", so these are results whose name contains those words, "
            + $"from the {scanned} most recent observations checked.";
        return new ClinicalList<ObservationSummary>(matches, more, matches.Count > 0 ? note : null);
    }

    /// <summary>Applies newest-first order, the latest-only grouping, and the result limit.</summary>
    private static ClinicalList<ObservationSummary> Shape(ClinicalList<ObservationSummary> result, ObservationQuery request)
    {
        var newestFirst = result.Items.OrderByDescending(o => o.Date ?? DateOnly.MinValue).ToList();

        if (!request.LatestOnly)
        {
            return new ClinicalList<ObservationSummary>(
                newestFirst.Take(request.MaxResults).ToList(),
                result.MoreAvailable || newestFirst.Count > request.MaxResults,
                result.Note);
        }

        var latest = newestFirst
            .GroupBy(o => o.LoincCode ?? o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ClinicalList<ObservationSummary>(
            latest.Take(request.MaxResults).ToList(),
            latest.Count > request.MaxResults || result.MoreAvailable,
            result.Note);
    }

    /// <summary>"Hemoglobin A1c/Hemoglobin.total in Blood" becomes [hemoglobin, a1c, hemoglobin, total, in, blood].</summary>
    private static List<string> NameMatcherWords(string value) =>
        WordSplit().Split(value)
            .Select(Matching.NameMatcher.Normalize)
            .Where(w => w.Length > 0)
            .ToList();

    [System.Text.RegularExpressions.GeneratedRegex("[^\\p{L}\\p{N}]+")]
    private static partial System.Text.RegularExpressions.Regex WordSplit();

    /// <summary>
    /// The Bundle's next-page link, only if it points back at the same server. Following a link
    /// to anywhere else would let a response steer this client to a different host.
    /// </summary>
    private string? NextPageUrl(JsonElement bundle)
    {
        var next = FhirJson.GetArray(bundle, "link")
            .Where(l => FhirJson.GetString(l, "relation") == "next")
            .Select(l => FhirJson.GetString(l, "url"))
            .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

        if (next is null)
        {
            return null;
        }

        return Uri.TryCreate(next, UriKind.Absolute, out var uri)
            && uri.AbsoluteUri.StartsWith(_http.BaseAddress!.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri
            : null;
    }

    /// <summary>Allergies and intolerances.</summary>
    public Task<ClinicalList<AllergySummary>> GetAllergiesAsync(
        string patientId, int maxResults = 50, CancellationToken cancellationToken = default)
    {
        ValidatePatientId(patientId);
        ValidateMax(maxResults);

        var query = new List<string> { "patient=" + patientId, "_count=" + maxResults };
        return SearchAsync("AllergyIntolerance", query, (a, _) => ClinicalMapper.MapAllergy(a), maxResults, cancellationToken);
    }

    /// <summary>
    /// One patient at a glance: demographics, active conditions and medications, allergies,
    /// latest vital signs and the most recent visit. Runs its six searches in parallel.
    /// </summary>
    public async Task<PatientOverview> GetPatientOverviewAsync(string patientId, CancellationToken cancellationToken = default)
    {
        ValidatePatientId(patientId);

        var patient = GetPatientAsync(patientId, cancellationToken);
        var conditions = GetConditionsAsync(patientId, activeOnly: true, maxResults: 50, cancellationToken);
        var medications = GetMedicationsAsync(patientId, activeOnly: true, maxResults: 50, cancellationToken);
        var allergies = GetAllergiesAsync(patientId, maxResults: 50, cancellationToken);
        var vitals = GetObservationsAsync(
            new ObservationQuery(patientId, Category: "vital-signs", LatestOnly: true, MaxResults: 20), cancellationToken);
        var encounters = GetEncountersAsync(patientId, maxResults: 1, cancellationToken: cancellationToken);

        // Surface the patient lookup's error (such as an unknown id) first, ahead of the others.
        var summary = await patient;
        await Task.WhenAll(conditions, medications, allergies, vitals, encounters);

        return new PatientOverview(
            summary,
            conditions.Result.Items,
            medications.Result.Items,
            allergies.Result.Items,
            vitals.Result.Items,
            encounters.Result.Items.FirstOrDefault());
    }

    private async Task<ClinicalList<T>> SearchAsync<T>(
        string resourceType,
        IEnumerable<string> query,
        Func<JsonElement, IReadOnlyDictionary<string, JsonElement>, T> map,
        int take,
        CancellationToken cancellationToken)
    {
        using var bundle = await GetJsonAsync(resourceType + "?" + string.Join('&', query), cancellationToken);
        var root = bundle.RootElement;

        var matches = ReadEntries(root, resourceType).ToList();
        var included = ReadAllEntries(root);
        var items = matches.Take(take).Select(m => map(m, included)).ToList();

        return new ClinicalList<T>(items, HasNextPage(root) || matches.Count > take);
    }

    /// <summary>Every resource in a Bundle keyed by "Type/id", for resolving references to included resources.</summary>
    private static Dictionary<string, JsonElement> ReadAllEntries(JsonElement bundle)
    {
        var all = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var entry in FhirJson.GetArray(bundle, "entry"))
        {
            if (FhirJson.GetObject(entry, "resource") is { } r
                && FhirJson.GetString(r, "resourceType") is { } type
                && FhirJson.GetString(r, "id") is { } id)
            {
                all.TryAdd($"{type}/{id}", r);
            }
        }
        return all;
    }

    /// <summary>
    /// Ids go into the URL path and query, so only well-formed FHIR ids are accepted. This keeps
    /// a model (or a prompt-injected value) from steering the request to another resource or server.
    /// </summary>
    /// <remarks>
    /// When the session is limited to one patient, any other id is refused here, before a request
    /// is sent. The FHIR server should enforce the same limit; checking here as well means this
    /// client does not depend on it (the SMART sandbox, for one, does not).
    /// </remarks>
    private void ValidatePatientId(string? patientId)
    {
        if (!FhirJson.IsValidId(patientId))
        {
            throw new ArgumentException(
                "patientId must be a FHIR id (letters, digits, '-' and '.', up to 64 characters). Use an id returned by a patient search.");
        }

        if (LockedPatientId is { } locked && !string.Equals(patientId, locked, StringComparison.Ordinal))
        {
            throw new PatientAccessDeniedException(patientId!, locked);
        }
    }

    private static void ValidateMax(int maxResults)
    {
        if (maxResults < 1 || maxResults > MaxListResults)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), $"maxResults must be between 1 and {MaxListResults}.");
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
}

/// <summary>Filters for an observation search.</summary>
/// <param name="Category">A FHIR observation category, such as laboratory or vital-signs.</param>
/// <param name="Code">A LOINC code ("4548-4") or system|code ("http://loinc.org|4548-4").</param>
/// <param name="Text">Words in the test's name, such as "glucose" or "a1c".</param>
/// <param name="Since">Only results on or after this date.</param>
/// <param name="LatestOnly">Keep only the most recent result of each test.</param>
public sealed record ObservationQuery(
    string PatientId,
    string? Category = null,
    string? Code = null,
    string? Text = null,
    DateOnly? Since = null,
    bool LatestOnly = false,
    int MaxResults = 20);
