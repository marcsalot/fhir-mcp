using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FhirMcp.Server;

/// <summary>
/// Shared JSON serializer options for all tool responses: camelCase, nulls omitted,
/// and relaxed encoding so non-ASCII characters in names and values are not escaped.
/// </summary>
internal static class ToolSerializer
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
