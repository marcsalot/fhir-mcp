using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FhirMcp.Core.Mapping;
using FhirMcp.Core.Matching;
using FhirMcp.Core.Models;

namespace FhirMcp.Core.Fhir;

/// <summary>
/// A small, read-only FHIR R4 client. It sends searches over REST, reads the
/// Bundle that comes back, and returns compact summaries instead of raw resources.
/// The <see cref="HttpClient"/> must have its BaseAddress set to the server's
/// base URL, ending in a slash (for example https://r4.smarthealthit.org/).
/// </summary>
public sealed class FhirApiClient
{
    private const string FhirJson = "application/fhir+json";

    private readonly HttpClient _http;
    private readonly TimeProvider _time;

    public FhirApiClient(HttpClient http, TimeProvider? time = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (_http.BaseAddress is null)
        {
            throw new ArgumentException("HttpClient.BaseAddress must be set to the FHIR server's base URL.", nameof(http));
        }
        _time = time ?? TimeProvider.System;
    }

    /// <summary>How many candidates to fetch when extra name words are checked on our side.</summary>
    internal const int CandidatePoolSize = 100;

    /// <summary>
    /// Searches for patients by name and/or birth date.
    ///
    /// FHIR servers match only one name value at a time, from its start, so the server is
    /// asked about the most specific word typed (prefix match, hyphens kept), and every word
    /// is then checked here against all of each candidate's names (see <see cref="NameMatcher"/>).
    /// If that finds nothing, the search is retried with the server's :contains modifier.
    /// </summary>
    public async Task<PatientSearchResult> SearchPatientsAsync(
        PatientSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        criteria.Validate();

        var tokens = string.IsNullOrWhiteSpace(criteria.Name)
            ? Array.Empty<string>()
            : NameMatcher.QueryTokens(criteria.Name);

        if (tokens.Count == 0)
        {
            var (patients, more) = await RunSearchAsync(criteria, anchor: null, tokens, NameMatchMode.None, criteria.MaxResults, cancellationToken);
            return new PatientSearchResult(patients, NameMatchMode.None, FellBackToContains: false, more);
        }

        var anchor = PickAnchor(criteria.Name!);
        var fetch = tokens.Count > 1 ? CandidatePoolSize : criteria.MaxResults;

        if (criteria.Contains)
        {
            var (patients, more) = await RunSearchAsync(criteria, anchor, tokens, NameMatchMode.Contains, fetch, cancellationToken);
            return new PatientSearchResult(patients, NameMatchMode.Contains, FellBackToContains: false, more);
        }

        var (startsWith, moreStartsWith) = await RunSearchAsync(criteria, anchor, tokens, NameMatchMode.StartsWith, fetch, cancellationToken);
        if (startsWith.Count > 0)
        {
            return new PatientSearchResult(startsWith, NameMatchMode.StartsWith, FellBackToContains: false, moreStartsWith);
        }

        var (contains, moreContains) = await RunSearchAsync(criteria, anchor, tokens, NameMatchMode.Contains, Math.Max(fetch, CandidatePoolSize), cancellationToken);
        return new PatientSearchResult(contains, NameMatchMode.Contains, FellBackToContains: true, moreContains);
    }

    private async Task<(IReadOnlyList<PatientSummary> Patients, bool MoreAvailable)> RunSearchAsync(
        PatientSearchCriteria criteria,
        string? anchor,
        IReadOnlyList<string> tokens,
        NameMatchMode mode,
        int fetch,
        CancellationToken cancellationToken)
    {
        var query = new List<string>();
        if (anchor is not null)
        {
            var parameter = mode == NameMatchMode.Contains ? "name:contains=" : "name=";
            query.Add(parameter + Uri.EscapeDataString(anchor));
        }
        if (criteria.BirthDate is { } birthDate)
        {
            query.Add("birthdate=" + birthDate.ToString("yyyy-MM-dd"));
        }
        query.Add("_count=" + fetch);

        using var bundle = await GetJsonAsync("Patient?" + string.Join('&', query), cancellationToken);
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        var contains = mode == NameMatchMode.Contains;

        var patients = ReadEntries(bundle.RootElement, "Patient")
            .Where(p => tokens.Count == 0 || NameMatcher.Matches(tokens, NameMatcher.PatientTokens(p), contains))
            .Select(p => PatientMapper.Map(p, today))
            .Take(criteria.MaxResults)
            .ToList();

        return (patients, HasNextPage(bundle.RootElement));
    }

    /// <summary>
    /// The word sent to the server: the longest one typed, as typed, so a hyphenated
    /// family name ("Al-Sayed") still matches the server's single stored value.
    /// </summary>
    internal static string PickAnchor(string name) =>
        name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .OrderByDescending(w => NameMatcher.Normalize(w).Length)
            .First();

    private static bool HasNextPage(JsonElement bundle) =>
        bundle.TryGetProperty("link", out var links)
        && links.ValueKind == JsonValueKind.Array
        && links.EnumerateArray().Any(l => l.TryGetProperty("relation", out var r) && r.GetString() == "next");

    /// <summary>Returns each entry.resource in a Bundle that has the given resourceType.</summary>
    internal static IEnumerable<JsonElement> ReadEntries(JsonElement bundle, string resourceType)
    {
        if (bundle.ValueKind != JsonValueKind.Object
            || !bundle.TryGetProperty("resourceType", out var type)
            || type.GetString() != "Bundle")
        {
            throw new FhirRequestException("The FHIR server did not return a Bundle.");
        }

        if (!bundle.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            yield break; // A search with no matches has no entry array.
        }

        foreach (var entry in entries.EnumerateArray())
        {
            // Search bundles can also carry OperationOutcome entries (warnings) and
            // included resources; keep only the type that was asked for.
            if (entry.TryGetProperty("resource", out var resource)
                && resource.TryGetProperty("resourceType", out var rt)
                && rt.GetString() == resourceType)
            {
                yield return resource;
            }
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(FhirJson));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FhirRequestException("The FHIR server did not respond in time.", inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new FhirRequestException($"Could not reach the FHIR server: {ex.Message}", inner: ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var detail = DescribeOperationOutcome(body);
                var message = $"The FHIR server returned {(int)response.StatusCode} {response.ReasonPhrase}"
                    + (detail is null ? "." : $": {detail}");
                throw new FhirRequestException(message, response.StatusCode);
            }

            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                throw new FhirRequestException("The FHIR server returned a response that is not valid JSON.", response.StatusCode, ex);
            }
        }
    }

    /// <summary>
    /// FHIR servers explain errors in an OperationOutcome. Pull out the human-readable
    /// parts so the model can tell the user what went wrong.
    /// </summary>
    internal static string? DescribeOperationOutcome(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("resourceType", out var rt)
                || rt.GetString() != "OperationOutcome"
                || !root.TryGetProperty("issue", out var issues)
                || issues.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var issue in issues.EnumerateArray())
            {
                var text = issue.TryGetProperty("diagnostics", out var d) && d.ValueKind == JsonValueKind.String
                    ? d.GetString()
                    : issue.TryGetProperty("details", out var det) && det.TryGetProperty("text", out var t)
                        ? t.GetString()
                        : null;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (sb.Length > 0) sb.Append("; ");
                    sb.Append(text.Trim());
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
