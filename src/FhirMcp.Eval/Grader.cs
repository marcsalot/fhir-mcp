using System.Globalization;
using System.Text.RegularExpressions;

namespace FhirMcp.Eval;

/// <summary>The verdict on one answer.</summary>
/// <param name="Problems">Why it failed, in plain words; empty when it passed.</param>
/// <param name="InventedMedications">Drugs the answer names that are not in this patient's record or the question.</param>
public sealed record Verdict(bool Passed, IReadOnlyList<string> Problems, IReadOnlyList<string> InventedMedications);

/// <summary>
/// Checks answers with fixed rules rather than another model, so a score means the same thing
/// on every run and anyone can see why an answer failed. The cost is that a correct answer in
/// unexpected words can fail; every failure is listed in the report for a person to review.
/// </summary>
public static partial class Grader
{
    public static Verdict Grade(Question question, string answer, string patientMedicationText)
    {
        var text = Normalize(answer);
        var problems = new List<string>();
        var g = question.Grading;

        if (g.StartsWith is { } expected)
        {
            var first = FirstWord(text);
            if (first != expected)
            {
                problems.Add($"expected the answer to start with \"{expected}\" but it starts with \"{first}\"");
            }
        }

        foreach (var group in g.All ?? [])
        {
            if (!group.Any(p => Matches(text, p)))
            {
                problems.Add($"missing: {string.Join(" or ", group.Select(Readable))}");
            }
        }

        foreach (var pattern in g.None ?? [])
        {
            if (Matches(text, pattern))
            {
                problems.Add($"mentions something not in the record: {Readable(pattern)}");
            }
        }

        if (g.Numbers is { Count: > 0 })
        {
            var numbers = NumbersIn(text);
            foreach (var group in g.Numbers)
            {
                if (!group.Any(e => numbers.Any(n => Math.Abs(n - e.Value) <= e.Tolerance + 1e-9)))
                {
                    problems.Add($"missing the value {string.Join(" or ", group.Select(e => e.Value.ToString(CultureInfo.InvariantCulture)))}");
                }
            }
        }

        var invented = DrugVocabulary.Invented(text, Normalize(question.Text), Normalize(patientMedicationText));
        if (invented.Count > 0)
        {
            problems.Add($"names medications not in the record: {string.Join(", ", invented)}");
        }

        return new Verdict(problems.Count == 0, problems, invented);
    }

    /// <summary>Lower case, without markdown emphasis or code marks, with plain spaces and dashes. Line breaks are kept, since they separate list items.</summary>
    internal static string Normalize(string answer) =>
        Whitespace().Replace(
            answer.ToLowerInvariant()
                .Replace('\u2019', '\'').Replace('\u2010', '-').Replace('\u2011', '-')
                .Replace('\u2013', '-').Replace('\u2014', '-').Replace('\u00a0', ' ')
                .Replace("**", "").Replace("__", "").Replace("`", ""),
            " ").Trim();

    /// <summary>The first word, ignoring leading punctuation, list marks and headings ("**No.**" is "no").</summary>
    internal static string FirstWord(string normalized)
    {
        var match = LeadingWord().Match(normalized);
        return match.Success ? match.Groups[1].Value : "";
    }

    /// <summary>Every number in the text. Thousands separators are removed; a blood pressure like 154/90 gives two numbers.</summary>
    internal static List<double> NumbersIn(string text) =>
        Number().Matches(text)
            .Select(m => double.Parse(m.Value.Replace(",", ""), CultureInfo.InvariantCulture))
            .ToList();

    private static bool Matches(string text, string pattern) =>
        Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>A pattern shown to a reader: the first alternative, without regex escapes.</summary>
    private static string Readable(string pattern) =>
        "\"" + pattern.Split('|')[0].Replace("\\", "").Replace(",?", ",").Replace(".?", ".") + "\"";

    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^[^a-z0-9]*([a-z0-9]+)")]
    private static partial Regex LeadingWord();

    [GeneratedRegex(@"(?<![\d.])\d{1,3}(?:,\d{3})+(?:\.\d+)?|(?<![\d.])\d+(?:\.\d+)?")]
    private static partial Regex Number();
}
