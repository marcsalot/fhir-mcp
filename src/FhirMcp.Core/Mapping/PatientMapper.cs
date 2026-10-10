using System.Globalization;
using System.Text.Json;
using FhirMcp.Core.Models;

namespace FhirMcp.Core.Mapping;

/// <summary>
/// Flattens a FHIR R4 Patient resource (as JSON) into a <see cref="PatientSummary"/>.
/// Works on raw JSON rather than a full object model so the core library has no
/// dependencies and only reads the handful of fields it needs.
/// </summary>
public static class PatientMapper
{
    public static PatientSummary Map(JsonElement patient, DateOnly today)
    {
        var id = GetString(patient, "id")
            ?? throw new JsonException("Patient resource has no id.");

        var birthDate = ParseFullDate(GetString(patient, "birthDate"));
        var deceased = IsDeceased(patient);
        var (city, state) = PickAddress(patient);

        return new PatientSummary(
            Id: id,
            Name: PickName(patient) ?? "(no name recorded)",
            Gender: GetString(patient, "gender"),
            BirthDate: birthDate,
            Age: birthDate is { } b && !deceased ? AgeOn(b, today) : null,
            City: city,
            State: state,
            Deceased: deceased);
    }

    /// <summary>Prefers the official name, then the usual name, then the first one listed.</summary>
    private static string? PickName(JsonElement patient)
    {
        if (!patient.TryGetProperty("name", out var names) || names.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var all = names.EnumerateArray().ToList();
        if (all.Count == 0)
        {
            return null;
        }

        var chosen = all.FirstOrDefault(n => GetString(n, "use") == "official");
        if (chosen.ValueKind == JsonValueKind.Undefined)
        {
            chosen = all.FirstOrDefault(n => GetString(n, "use") == "usual");
        }
        if (chosen.ValueKind == JsonValueKind.Undefined)
        {
            chosen = all[0];
        }

        var text = GetString(chosen, "text");
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        var given = chosen.TryGetProperty("given", out var g) && g.ValueKind == JsonValueKind.Array
            ? g.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x))
            : Enumerable.Empty<string?>();

        var parts = given.Append(GetString(chosen, "family")).Where(x => !string.IsNullOrWhiteSpace(x));
        var joined = string.Join(' ', parts);
        return joined.Length > 0 ? joined : null;
    }

    /// <summary>Prefers the home address, then the first one listed.</summary>
    private static (string? City, string? State) PickAddress(JsonElement patient)
    {
        if (!patient.TryGetProperty("address", out var addresses) || addresses.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        var all = addresses.EnumerateArray().ToList();
        if (all.Count == 0)
        {
            return (null, null);
        }

        var home = all.FirstOrDefault(a => GetString(a, "use") == "home");
        var chosen = home.ValueKind == JsonValueKind.Undefined ? all[0] : home;
        return (GetString(chosen, "city"), GetString(chosen, "state"));
    }

    private static bool IsDeceased(JsonElement patient)
    {
        if (patient.TryGetProperty("deceasedBoolean", out var flag))
        {
            return flag.ValueKind == JsonValueKind.True;
        }

        return patient.TryGetProperty("deceasedDateTime", out _);
    }

    /// <summary>
    /// FHIR dates can be partial (YYYY or YYYY-MM). Only full dates are used,
    /// so the model is never shown an age that looks more precise than the data.
    /// </summary>
    private static DateOnly? ParseFullDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static int AgeOn(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        if (today < birthDate.AddYears(age))
        {
            age--;
        }
        return Math.Max(age, 0);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
