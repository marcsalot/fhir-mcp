namespace FhirMcp.Core.Models;

/// <summary>A page of compact records, plus whether the server had more than were returned.</summary>
/// <param name="Note">Anything the reader should know about how the items were found.</param>
public sealed record ClinicalList<T>(IReadOnlyList<T> Items, bool MoreAvailable, string? Note = null);

/// <summary>A medication order (FHIR MedicationRequest).</summary>
/// <param name="Status">active, completed, stopped, on-hold, cancelled, entered-in-error, draft or unknown.</param>
/// <param name="RxNormCode">The RxNorm code when the order carries one.</param>
public sealed record MedicationSummary(
    string Name,
    string? Status,
    DateOnly? Ordered,
    string? Dosage,
    string? Reason,
    string? RxNormCode);

/// <summary>A problem or diagnosis (FHIR Condition).</summary>
/// <param name="ClinicalStatus">active, recurrence, relapse, inactive, remission or resolved.</param>
/// <param name="SnomedCode">The SNOMED CT code when the condition carries one.</param>
public sealed record ConditionSummary(
    string Name,
    string? ClinicalStatus,
    string? VerificationStatus,
    DateOnly? Onset,
    DateOnly? Resolved,
    string? SnomedCode);

/// <summary>A visit or stay (FHIR Encounter).</summary>
/// <param name="Setting">Where it happened: ambulatory, emergency, inpatient, virtual and so on.</param>
public sealed record EncounterSummary(
    DateOnly? Start,
    DateOnly? End,
    string? Setting,
    string? Type,
    IReadOnlyList<string> Reasons,
    string? Status,
    string? DischargedTo);

/// <summary>A lab result, vital sign or survey answer (FHIR Observation).</summary>
/// <param name="Value">The result as a person would read it: "77.93 mg/dL", "Never smoker", "Systolic 106.4 mm[Hg]; Diastolic 79.69 mm[Hg]".</param>
/// <param name="Interpretation">The lab's flag, such as High or Low, when recorded.</param>
/// <param name="LoincCode">The LOINC code, which identifies the test.</param>
public sealed record ObservationSummary(
    string Name,
    DateOnly? Date,
    string? Value,
    string? Interpretation,
    string? ReferenceRange,
    string? Category,
    string? LoincCode);

/// <summary>An allergy or intolerance (FHIR AllergyIntolerance).</summary>
public sealed record AllergySummary(
    string Substance,
    string? ClinicalStatus,
    string? Criticality,
    IReadOnlyList<string> Reactions,
    DateOnly? Recorded);

/// <summary>One patient at a glance: who they are and what is current.</summary>
/// <param name="LatestVitals">The most recent reading of each vital sign.</param>
/// <param name="LastEncounter">The most recent visit.</param>
public sealed record PatientOverview(
    PatientSummary Patient,
    IReadOnlyList<ConditionSummary> ActiveConditions,
    IReadOnlyList<MedicationSummary> ActiveMedications,
    IReadOnlyList<AllergySummary> Allergies,
    IReadOnlyList<ObservationSummary> LatestVitals,
    EncounterSummary? LastEncounter);
