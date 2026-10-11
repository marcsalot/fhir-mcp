using System.Globalization;
using FhirMcp.Core.Fhir;
using ModelContextProtocol;

namespace FhirMcp.Server.Tools;

/// <summary>
/// Turns expected failures into <see cref="McpException"/>, whose message reaches the model,
/// so it can fix its input or explain the problem. Anything unexpected stays hidden behind
/// the SDK's generic error.
/// </summary>
internal static class ToolErrors
{
    public static async Task<T> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is ArgumentException or FhirRequestException)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    public static DateOnly? ParseDate(string? value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new McpException($"{parameter} must be formatted yyyy-MM-dd, for example 2020-01-31. Got \"{value}\".");
    }

    /// <summary>"active" means current items only; "all" includes past ones.</summary>
    public static bool ActiveOnly(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        null or "" or "all" => false,
        "active" => true,
        _ => throw new McpException($"status must be \"active\" or \"all\". Got \"{status}\"."),
    };
}
