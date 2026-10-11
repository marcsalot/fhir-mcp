using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FhirMcp.Core.Fhir;

/// <summary>Small helpers for reading the FHIR JSON shapes that recur across resource types.</summary>
public static partial class FhirJson
{
    public static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static JsonElement? GetObject(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    public static IEnumerable<JsonElement> GetArray(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    /// <summary>
    /// The readable label of a CodeableConcept: its text, else the first coding's display,
    /// else the first coding's code.
    /// </summary>
    public static string? ConceptText(JsonElement? concept)
    {
        if (concept is not { } c || c.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var text = GetString(c, "text");
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        foreach (var coding in GetArray(c, "coding"))
        {
            var display = GetString(coding, "display");
            if (!string.IsNullOrWhiteSpace(display)) return display.Trim();
        }

        return GetArray(c, "coding").Select(x => GetString(x, "code")).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    /// <summary>The first coding's code in a CodeableConcept, optionally only from one code system.</summary>
    public static string? ConceptCode(JsonElement? concept, string? system = null)
    {
        if (concept is not { } c || c.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetArray(c, "coding")
            .Where(x => system is null || GetString(x, "system") == system)
            .Select(x => GetString(x, "code"))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    /// <summary>The date part of a FHIR date or dateTime, when it has a full year, month and day.</summary>
    public static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 10)
        {
            return null;
        }

        return DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    /// <summary>FHIR resource ids: letters, digits, '-' and '.', up to 64 characters.</summary>
    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    [GeneratedRegex("^[A-Za-z0-9\\-\\.]{1,64}$")]
    private static partial Regex IdPattern();

    /// <summary>
    /// Formats a measured number for a person to read. Synthetic data carries values like
    /// 77.92600836244159; four significant digits is more than any lab reports.
    /// </summary>
    public static string FormatNumber(double value)
    {
        if (Math.Abs(value) >= 10000)
        {
            return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
        }

        return value.ToString("G4", CultureInfo.InvariantCulture);
    }
}
