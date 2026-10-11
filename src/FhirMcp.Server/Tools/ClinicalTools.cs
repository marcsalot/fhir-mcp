using System.ComponentModel;
using FhirMcp.Core.Fhir;
using FhirMcp.Core.Models;
using ModelContextProtocol.Server;

namespace FhirMcp.Server.Tools;

/// <summary>MCP tools that read a patient's clinical record. All read-only.</summary>
[McpServerToolType]
public sealed class ClinicalTools
{
    private const string PatientIdHelp = "The patient's id, from search_patients.";
    private const string MaxHelp = "Maximum items to return, 1 to 100. Defaults to 20.";

    private readonly FhirApiClient _fhir;
    private readonly ToolOutput _output;

    public ClinicalTools(FhirApiClient fhir, ToolOutput output)
    {
        _fhir = fhir;
        _output = output;
    }

    [McpServerTool(Name = "get_patient_summary", Title = "Patient summary", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description(
        "One patient at a glance: demographics, active conditions, active medications, allergies, " +
        "the latest reading of each vital sign, and the most recent visit. Start here to answer a general " +
        "question about a patient, then use the other tools for history or detail.")]
    public Task<string> GetPatientSummary(
        [Description(PatientIdHelp)] string patientId,
        CancellationToken cancellationToken = default) =>
        ToolErrors.Run(async () => _output.Serialize(
            await _fhir.GetPatientOverviewAsync(patientId, cancellationToken)));

    [McpServerTool(Name = "get_medications", Title = "Medications", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description(
        "The patient's medication orders, newest first, with status, order date, dosage, reason, and RxNorm code. " +
        "Use status \"active\" for what the patient is currently prescribed, or \"all\" to include past orders.")]
    public Task<string> GetMedications(
        [Description(PatientIdHelp)] string patientId,
        [Description("\"active\" for current orders only, or \"all\" (the default) to include stopped and completed ones.")] string? status = null,
        [Description(MaxHelp)] int maxResults = 20,
        CancellationToken cancellationToken = default) =>
        ToolErrors.Run(async () => _output.Serialize(ListResponse.From(
            patientId,
            await _fhir.GetMedicationsAsync(patientId, ToolErrors.ActiveOnly(status), maxResults, cancellationToken),
            emptyNote: ToolErrors.ActiveOnly(status) ? "No active medication orders. Try status \"all\" for past orders." : "No medication orders on record.")));

    [McpServerTool(Name = "get_conditions", Title = "Conditions", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description(
        "The patient's problems and diagnoses, most recent onset first, with clinical status, onset and resolution dates, " +
        "and SNOMED CT code. Use status \"active\" for current problems (including recurrences and relapses), or \"all\" for history.")]
    public Task<string> GetConditions(
        [Description(PatientIdHelp)] string patientId,
        [Description("\"active\" for current problems only, or \"all\" (the default) to include resolved ones.")] string? status = null,
        [Description(MaxHelp)] int maxResults = 20,
        CancellationToken cancellationToken = default) =>
        ToolErrors.Run(async () => _output.Serialize(ListResponse.From(
            patientId,
            await _fhir.GetConditionsAsync(patientId, ToolErrors.ActiveOnly(status), maxResults, cancellationToken),
            emptyNote: "No conditions on record.")));

    [McpServerTool(Name = "get_encounters", Title = "Encounters", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description(
        "The patient's visits and hospital stays, newest first: dates, setting (ambulatory, emergency, inpatient, virtual), " +
        "visit type, reasons, and where an inpatient was discharged to.")]
    public Task<string> GetEncounters(
        [Description(PatientIdHelp)] string patientId,
        [Description("Only visits on or after this date, formatted yyyy-MM-dd.")] string? since = null,
        [Description(MaxHelp)] int maxResults = 20,
        CancellationToken cancellationToken = default) =>
        ToolErrors.Run(async () => _output.Serialize(ListResponse.From(
            patientId,
            await _fhir.GetEncountersAsync(patientId, ToolErrors.ParseDate(since, nameof(since)), maxResults, cancellationToken),
            emptyNote: "No visits found for that period.")));

    [McpServerTool(Name = "get_observations", Title = "Lab results and vital signs", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description(
        "The patient's lab results, vital signs, and other observations, newest first, with readable values and units. " +
        "Narrow with category (laboratory, vital-signs, social-history, survey, exam, imaging, procedure, therapy, activity), " +
        "a test name (text: \"glucose\", \"a1c\", \"cholesterol\"), a LOINC code, or a start date. " +
        "Set latestOnly to get just the most recent result of each test, which suits \"what are the latest labs?\". " +
        "To see a trend, search one test by name or code without latestOnly.")]
    public Task<string> GetObservations(
        [Description(PatientIdHelp)] string patientId,
        [Description("An observation category, such as laboratory or vital-signs.")] string? category = null,
        [Description("Words in the test's name, such as \"glucose\" or \"hemoglobin a1c\".")] string? text = null,
        [Description("A LOINC code such as 4548-4, or system|code.")] string? code = null,
        [Description("Only results on or after this date, formatted yyyy-MM-dd.")] string? since = null,
        [Description("Keep only the most recent result of each test.")] bool latestOnly = false,
        [Description(MaxHelp)] int maxResults = 20,
        CancellationToken cancellationToken = default) =>
        ToolErrors.Run(async () => _output.Serialize(ListResponse.From(
            patientId,
            await _fhir.GetObservationsAsync(
                new ObservationQuery(
                    patientId,
                    string.IsNullOrWhiteSpace(category) ? null : category.Trim().ToLowerInvariant(),
                    code,
                    text,
                    ToolErrors.ParseDate(since, nameof(since)),
                    latestOnly,
                    maxResults),
                cancellationToken),
            emptyNote: "No matching observations. Try a broader name, no category, or no date.")));

    [McpServerTool(Name = "get_allergies", Title = "Allergies", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("The patient's allergies and intolerances, with status, criticality, and recorded reactions.")]
    public Task<string> GetAllergies(
        [Description(PatientIdHelp)] string patientId,
        CancellationToken cancellationToken = default) =>
        ToolErrors.Run(async () => _output.Serialize(ListResponse.From(
            patientId,
            await _fhir.GetAllergiesAsync(patientId, maxResults: 50, cancellationToken),
            emptyNote: "No allergies recorded. That means none are on file, not that the patient has none.")));
}

/// <summary>What the model sees for a list: the items plus a plain note when there is something to know.</summary>
public sealed record ListResponse<T>(string PatientId, int Count, bool MoreAvailable, string? Note, IReadOnlyList<T> Items);

public static class ListResponse
{
    public static ListResponse<T> From<T>(string patientId, ClinicalList<T> list, string emptyNote)
    {
        var notes = new List<string>();
        if (list.Note is not null)
        {
            notes.Add(list.Note);
        }
        if (list.Items.Count == 0)
        {
            notes.Add(emptyNote);
        }
        else if (list.MoreAvailable)
        {
            notes.Add("More items exist than were returned. Raise maxResults or narrow the search to see them.");
        }

        return new ListResponse<T>(
            patientId, list.Items.Count, list.MoreAvailable, notes.Count > 0 ? string.Join(' ', notes) : null, list.Items);
    }
}
