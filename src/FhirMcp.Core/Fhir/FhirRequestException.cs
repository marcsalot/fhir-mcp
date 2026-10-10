using System.Net;

namespace FhirMcp.Core.Fhir;

/// <summary>
/// Raised when the FHIR server returns an error or a response that is not valid FHIR JSON.
/// The message is written to be safe to show to the model (no stack traces, no raw payloads).
/// </summary>
public sealed class FhirRequestException : Exception
{
    public FhirRequestException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
