namespace FhirMcp.Core.Fhir;

/// <summary>
/// Criteria for a Patient search. At least one of <see cref="Name"/> or
/// <see cref="BirthDate"/> is required so a tool call can never page through
/// every patient on the server.
/// </summary>
/// <param name="Name">Any part of a name, in any order: "Adolph Smith", "smith", "Al-Sayed".</param>
/// <param name="BirthDate">An exact birth date.</param>
/// <param name="MaxResults">How many patients to return, 1 to 50.</param>
/// <param name="Contains">Match name words anywhere instead of at the start. Slower and looser.</param>
public sealed record PatientSearchCriteria(
    string? Name = null,
    DateOnly? BirthDate = null,
    int MaxResults = 10,
    bool Contains = false)
{
    public const int Limit = 50;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) && BirthDate is null)
        {
            throw new ArgumentException("Provide a name, a birth date, or both.");
        }

        if (Name is not null && Matching.NameMatcher.QueryTokens(Name).Count == 0 && BirthDate is null)
        {
            throw new ArgumentException("The name has no letters or digits to search on.");
        }

        if (MaxResults < 1 || MaxResults > Limit)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxResults), $"MaxResults must be between 1 and {Limit}.");
        }
    }
}
