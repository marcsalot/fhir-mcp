using System.ComponentModel;
using System.Globalization;
using FhirMcp.Core.Fhir;
using FhirMcp.Core.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FhirMcp.Server.Tools;

/// <summary>MCP tools for finding patients. Read-only.</summary>
[McpServerToolType]
public sealed class PatientTools
{
    private readonly FhirApiClient _fhir;
    private readonly ToolOutput _output;

    public PatientTools(FhirApiClient fhir, ToolOutput output)
    {
        _fhir = fhir;
        _output = output;
    }

    [McpServerTool(Name = "search_patients", Title = "Search patients", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description(
        "Find patients by name and/or birth date on the connected FHIR server (synthetic test data). " +
        "Returns compact summaries with each patient's id, which the other tools take as input. " +
        "Name matching: type any part of a name in any order (\"Reyna Ziemann\", \"ziemann\", \"Al-Sayed\", \"al sayed\"). " +
        "Each word must match the START of a word in one of the patient's names, including maiden and other names, " +
        "ignoring case, accents, hyphens and apostrophes. So \"smith\" also finds \"Smitham\", but \"ham\" does not. " +
        "Type hyphens and apostrophes as stored (\"O'Keefe\", not \"okeefe\"); the server cannot match across them. " +
        "If nothing matches, the search is retried automatically matching words anywhere in a name, and the result says so. " +
        "When a patient matched on a name other than their current one, it appears in otherNames.")]
    public async Task<string> SearchPatients(
        [Description("Any part of the patient's name, in any order. Optional if birthDate is given.")]
        string? name = null,
        [Description("Exact birth date, formatted yyyy-MM-dd. Optional if name is given.")]
        string? birthDate = null,
        [Description("Match name words anywhere, not just at the start. Use only when a normal search finds nothing.")]
        bool contains = false,
        [Description("Maximum patients to return, 1 to 50. Defaults to 10.")]
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        DateOnly? parsedBirthDate = null;
        if (!string.IsNullOrWhiteSpace(birthDate))
        {
            if (!DateOnly.TryParseExact(birthDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                throw new McpException($"birthDate must be formatted yyyy-MM-dd, for example 1970-01-31. Got \"{birthDate}\".");
            }
            parsedBirthDate = d;
        }

        try
        {
            var result = await _fhir.SearchPatientsAsync(
                new PatientSearchCriteria(name, parsedBirthDate, maxResults, contains),
                cancellationToken);

            return _output.Serialize(PatientSearchResponse.From(result));
        }
        catch (Exception ex) when (ex is ArgumentException or FhirRequestException)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}

/// <summary>What the model sees: the patients plus a plain note on how they were found.</summary>
public sealed record PatientSearchResponse(
    int Count,
    string MatchedBy,
    string? Note,
    IReadOnlyList<PatientSummary> Patients)
{
    public static PatientSearchResponse From(PatientSearchResult result)
    {
        var matchedBy = result.MatchMode switch
        {
            NameMatchMode.None => "birth date",
            NameMatchMode.StartsWith => "name (start of words)",
            NameMatchMode.Contains => "name (anywhere in words)",
            _ => result.MatchMode.ToString(),
        };

        var notes = new List<string>();
        if (result.Patients.Count == 0)
        {
            notes.Add(result.FellBackToContains
                ? "No patients matched, even matching the words anywhere in a name. Try fewer words, a different spelling (names are stored with their hyphens and apostrophes, as in \"Al-Sayed\" or \"O'Keefe\"), or a birth date."
                : "No patients matched. Try fewer words, a different spelling, or a birth date.");
        }
        else if (result.FellBackToContains)
        {
            notes.Add("No name started with the words given, so these match the words anywhere in a name. Check that they are the patient you mean.");
        }
        if (result.MoreAvailable)
        {
            notes.Add("The server has more possible matches than were checked. Add a birth date or more of the name to narrow the search.");
        }

        return new PatientSearchResponse(
            result.Patients.Count,
            matchedBy,
            notes.Count > 0 ? string.Join(' ', notes) : null,
            result.Patients);
    }
}
