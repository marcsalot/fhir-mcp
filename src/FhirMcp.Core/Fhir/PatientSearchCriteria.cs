namespace FhirMcp.Core.Fhir;

/// <summary>
/// Criteria for a Patient search. At least one of <see cref="Name"/> or
/// <see cref="BirthDate"/> is required so a tool call can never page through
/// every patient on the server.
/// </summary>
public sealed record PatientSearchCriteria(string? Name = null, DateOnly? BirthDate = null, int MaxResults = 10)
{
    public const int Limit = 50;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) && BirthDate is null)
        {
            throw new ArgumentException("Provide a name, a birth date, or both.");
        }

        if (MaxResults < 1 || MaxResults > Limit)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxResults), $"MaxResults must be between 1 and {Limit}.");
        }
    }
}
