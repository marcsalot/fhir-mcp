namespace FhirMcp.Core.Models;

/// <summary>How a patient search found its results.</summary>
public enum NameMatchMode
{
    /// <summary>No name was given; the search used the birth date only.</summary>
    None,

    /// <summary>Each typed word matched the start of a word in the patient's names.</summary>
    StartsWith,

    /// <summary>Words matched anywhere in a name. Used on request, or when a starts-with search found nothing.</summary>
    Contains,
}

/// <param name="Patients">The matching patients, capped at the requested maximum.</param>
/// <param name="MatchMode">How names were matched.</param>
/// <param name="FellBackToContains">True when a starts-with search found nothing and the search was retried with contains.</param>
/// <param name="MoreAvailable">True when the server had more candidates than were checked, so a narrower search may find more.</param>
public sealed record PatientSearchResult(
    IReadOnlyList<PatientSummary> Patients,
    NameMatchMode MatchMode,
    bool FellBackToContains,
    bool MoreAvailable);
