using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FhirMcp.Core.Matching;

/// <summary>
/// Matches what a person types ("Fatima Al Sayed", "al-sayed", "Arisso") against a
/// patient's recorded names, the way a person would expect rather than the way a
/// FHIR server does.
///
/// FHIR's default string search matches only the start of each name part, and treats
/// a hyphenated family name ("Al-Sayed") as one value, so "Sayed" and "Al Sayed" miss.
/// Here, every word the user typed must be the start of some word in any of the
/// patient's names (official, maiden, nickname). Matching ignores case and accents,
/// treats hyphens and spaces alike, and also accepts a multi-part name typed as one
/// word ("AlSayed", "OBrien").
/// </summary>
public static class NameMatcher
{
    private static readonly char[] Separators = { ' ', '\t', '-', '‐', '‑', '–' };

    /// <summary>Splits a search into normalized words. "Al-Sayed  Fatima" becomes [al, sayed, fatima].</summary>
    public static IReadOnlyList<string> QueryTokens(string query) =>
        query.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(t => t.Length > 0)
            .ToList();

    /// <summary>
    /// Every word in every name the patient has, normalized, plus each multi-word part
    /// joined into one word. "Al-Sayed" gives al, sayed and alsayed.
    /// </summary>
    public static HashSet<string> PatientTokens(JsonElement patient)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (!patient.TryGetProperty("name", out var names) || names.ValueKind != JsonValueKind.Array)
        {
            return tokens;
        }

        foreach (var name in names.EnumerateArray())
        {
            foreach (var part in NameParts(name))
            {
                var words = part.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Normalize)
                    .Where(w => w.Length > 0)
                    .ToList();

                tokens.UnionWith(words);
                if (words.Count > 1)
                {
                    tokens.Add(string.Concat(words));
                }
            }
        }

        return tokens;
    }

    /// <summary>
    /// True when every query word is the start of at least one of the patient's name words,
    /// or, with <paramref name="contains"/>, appears anywhere in one.
    /// </summary>
    public static bool Matches(IReadOnlyList<string> queryTokens, IReadOnlySet<string> patientTokens, bool contains = false) =>
        queryTokens.All(q => patientTokens.Any(p => contains
            ? p.Contains(q, StringComparison.Ordinal)
            : p.StartsWith(q, StringComparison.Ordinal)));

    /// <summary>Lowercases, strips accents, and drops punctuation such as apostrophes and periods.</summary>
    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }

    private static IEnumerable<string> NameParts(JsonElement name)
    {
        foreach (var field in new[] { "text", "family" })
        {
            if (name.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } s)
            {
                yield return s;
            }
        }

        foreach (var field in new[] { "given", "prefix", "suffix" })
        {
            if (name.TryGetProperty(field, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                    {
                        yield return s;
                    }
                }
            }
        }
    }
}
