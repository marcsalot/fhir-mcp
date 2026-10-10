namespace FhirMcp.Core.Models;

/// <summary>
/// A compact view of a FHIR Patient: only the fields a model needs to identify
/// a patient and pick the right one. Raw FHIR Patient resources are large;
/// this keeps tool responses small and easy to reason about.
/// </summary>
/// <param name="OtherNames">
/// Names besides the one shown, with their use, such as "Melony Hammes (maiden)".
/// They explain why a patient matched a search for a name that is not their current one.
/// </param>
public sealed record PatientSummary(
    string Id,
    string Name,
    IReadOnlyList<string> OtherNames,
    string? Gender,
    DateOnly? BirthDate,
    int? Age,
    string? City,
    string? State,
    bool Deceased);
