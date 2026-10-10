namespace FhirMcp.Core.Models;

/// <summary>
/// A compact view of a FHIR Patient: only the fields a model needs to identify
/// a patient and pick the right one. Raw FHIR Patient resources are large;
/// this keeps tool responses small and easy to reason about.
/// </summary>
public sealed record PatientSummary(
    string Id,
    string Name,
    string? Gender,
    DateOnly? BirthDate,
    int? Age,
    string? City,
    string? State,
    bool Deceased);
