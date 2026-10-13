using System.Globalization;
using System.Text;

namespace FhirMcp.Eval;

/// <summary>Turns results into a markdown report: a scoreboard, scores by category, and every failure.</summary>
public static class Report
{
    public sealed record RunSummary(
        string Label,
        int Questions,
        int Passed,
        double Score,
        int CriticalMissed,
        int InventedMedications,
        int Errors,
        double MedianSeconds,
        long InputTokens,
        long OutputTokens);

    public static IReadOnlyList<RunSummary> Summarize(IReadOnlyList<QuestionResult> results) =>
        results.GroupBy(r => r.RunLabel)
            .Select(g =>
            {
                var list = g.ToList();
                var seconds = list.Select(r => r.Seconds).Order().ToList();
                return new RunSummary(
                    g.Key,
                    list.Count,
                    list.Count(r => r.Passed),
                    100.0 * list.Where(r => r.Passed).Sum(r => r.Weight) / list.Sum(r => r.Weight),
                    list.Count(r => r.Critical && !r.Passed),
                    list.Count(r => r.InventedMedications.Count > 0),
                    list.Count(r => r.Error is not null),
                    seconds.Count == 0 ? 0 : seconds[seconds.Count / 2],
                    list.Sum(r => (long)(r.InputTokens ?? 0)),
                    list.Sum(r => (long)(r.OutputTokens ?? 0)));
            })
            .OrderByDescending(s => s.Score)
            .ToList();

    public static string Build(
        IReadOnlyList<QuestionResult> results,
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, string> modelNotes,
        string commandLine,
        DateTimeOffset when)
    {
        var sb = new StringBuilder();
        var summaries = Summarize(results);
        var byId = questions.ToDictionary(q => q.Id);

        sb.AppendLine("# Evaluation report");
        sb.AppendLine();
        sb.AppendLine($"Run {when:yyyy-MM-dd HH:mm} UTC with `{commandLine}`.");
        sb.AppendLine($"{questions.Count} questions about {questions.Select(q => q.Patient).Distinct().Count()} synthetic patients; " +
                      $"{questions.Count(q => q.Critical)} are medication or allergy questions, which count three times.");
        sb.AppendLine();
        if (modelNotes.Count > 0)
        {
            sb.AppendLine("Models:");
            sb.AppendLine();
            foreach (var (name, note) in modelNotes)
            {
                sb.AppendLine($"- `{name}`: {note}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Scoreboard");
        sb.AppendLine();
        sb.AppendLine("Score weights each question by importance (medication and allergy questions count three times). " +
                      "A critical miss is a wrong medication or allergy answer. Invented medications counts answers that named a drug not in the patient's record.");
        sb.AppendLine();
        sb.AppendLine("| Model / mode / privacy | Score | Passed | Critical misses | Invented medications | Errors | Median time | Tokens in / out |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var s in summaries)
        {
            sb.AppendLine($"| {s.Label} | {s.Score:0.0}% | {s.Passed}/{s.Questions} | {s.CriticalMissed} | {s.InventedMedications} | {s.Errors} | " +
                          $"{s.MedianSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s | {s.InputTokens:N0} / {s.OutputTokens:N0} |");
        }
        sb.AppendLine();

        var categories = questions.Select(q => q.Category).Distinct().Order().ToList();
        var labels = summaries.Select(s => s.Label).ToList();
        sb.AppendLine("## By category");
        sb.AppendLine();
        sb.AppendLine("| Category | " + string.Join(" | ", labels) + " |");
        sb.AppendLine("| --- | " + string.Join(" | ", labels.Select(_ => "---")) + " |");
        foreach (var category in categories)
        {
            var cells = labels.Select(label =>
            {
                var rows = results.Where(r => r.RunLabel == label && r.Category == category).ToList();
                return rows.Count == 0 ? "" : $"{rows.Count(r => r.Passed)}/{rows.Count}";
            });
            sb.AppendLine($"| {category} | " + string.Join(" | ", cells) + " |");
        }
        sb.AppendLine();
        sb.AppendLine("De-identified runs reduce every date to its year, so questions that ask for a full date are expected to fail there. That is the measured cost of the privacy mode, not a model error.");
        sb.AppendLine();

        sb.AppendLine("## Failures");
        sb.AppendLine();
        sb.AppendLine("Every failed answer, for a person to review. Fixed rules can mark a correct answer wrong when it uses unexpected words; if so, widen the question's patterns rather than trusting the score.");
        sb.AppendLine();
        var failures = results.Where(r => !r.Passed)
            .OrderBy(r => questions.ToList().FindIndex(q => q.Id == r.QuestionId))
            .ThenBy(r => r.RunLabel)
            .GroupBy(r => r.QuestionId);
        var any = false;
        foreach (var group in failures)
        {
            any = true;
            var q = byId[group.Key];
            sb.AppendLine($"### {q.Id}{(q.Critical ? " (critical)" : "")}");
            sb.AppendLine();
            sb.AppendLine($"> {q.Text}");
            sb.AppendLine();
            foreach (var r in group)
            {
                sb.AppendLine($"- **{r.RunLabel}**: {string.Join("; ", r.Problems)}");
                if (!string.IsNullOrWhiteSpace(r.Answer))
                {
                    sb.AppendLine($"  - Answer: \"{Excerpt(r.Answer, 300)}\"");
                }
                if (r.ToolCalls.Count > 0)
                {
                    sb.AppendLine($"  - Tool calls: {string.Join(", ", r.ToolCalls.Select(c => c.Name + (c.IsError ? " (error)" : "")))}");
                }
            }
            sb.AppendLine();
        }
        if (!any)
        {
            sb.AppendLine("None.");
        }

        return sb.ToString();
    }

    private static string Excerpt(string text, int max)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Replace("\"", "'");
        return flat.Length <= max ? flat : flat[..max] + "...";
    }
}
