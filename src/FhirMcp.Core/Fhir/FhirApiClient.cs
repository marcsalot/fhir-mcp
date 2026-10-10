using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FhirMcp.Core.Mapping;
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

    /// <summary>Searches for patients by name and/or birth date.</summary>
    public async Task<IReadOnlyList<PatientSummary>> SearchPatientsAsync(
        PatientSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        criteria.Validate();

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(criteria.Name))
        {
            query.Add("name=" + Uri.EscapeDataString(criteria.Name.Trim()));
        }
        if (criteria.BirthDate is { } birthDate)
        {
            query.Add("birthdate=" + birthDate.ToString("yyyy-MM-dd"));
        }
        query.Add("_count=" + criteria.MaxResults);

        using var bundle = await GetJsonAsync("Patient?" + string.Join('&', query), cancellationToken);
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

        return ReadEntries(bundle.RootElement, "Patient")
            .Select(p => PatientMapper.Map(p, today))
            .Take(criteria.MaxResults)
            .ToList();
    }

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
