namespace FhirMcp.Core.Fhir;

/// <summary>
/// Limits which records the client may read, on top of what the FHIR server itself enforces.
/// </summary>
public interface IAccessScope
{
    /// <summary>
    /// The only patient this session may read, when sign-in granted patient-level access to one
    /// patient. Null means no limit beyond the server's own.
    /// </summary>
    string? LockedPatientId { get; }
}

/// <summary>Raised when a request asks about a patient outside the session's access.</summary>
public sealed class PatientAccessDeniedException : Exception
{
    public const string MessagePrefix = "Access denied:";

    public PatientAccessDeniedException(string requestedId, string allowedId)
        : base($"{MessagePrefix} this session is limited to the patient selected at sign-in (id {allowedId}). " +
               $"Requests about other patients, including {requestedId}, are refused. Sign in again and select a different patient to see another record.")
    {
    }
}
