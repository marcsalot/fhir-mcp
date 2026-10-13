using System.Text.Json;
using System.Text.Json.Serialization;

namespace FhirMcp.Eval;

/// <summary>One accepted number, such as 5.96 within 0.011.</summary>
public sealed record ExpectedNumber(double Value, double Tolerance);

/// <summary>
/// How an answer is checked. Every rule that is set must pass. Patterns are case-insensitive
/// regular expressions matched against the answer.
/// </summary>
/// <param name="StartsWith">"yes" or "no": the answer must begin with that word.</param>
/// <param name="All">Each group must match: at least one pattern in every group appears in the answer.</param>
/// <param name="None">No pattern may appear in the answer (things that are not in the record).</param>
/// <param name="Numbers">Each group must match: some number in the answer is within tolerance of one of the group's values (alternatives cover units, such as kg or lb).</param>
public sealed record Grading(
    string? StartsWith = null,
    IReadOnlyList<IReadOnlyList<string>>? All = null,
    IReadOnlyList<string>? None = null,
    IReadOnlyList<IReadOnlyList<ExpectedNumber>>? Numbers = null);

/// <param name="Patient">The context file in eval/context that holds this patient's record.</param>
/// <param name="Critical">Medication and allergy questions: a miss counts three times and is listed as a critical miss.</param>
/// <param name="Reference">A correct answer written from the record; a test checks that it passes the grading.</param>
public sealed record Question(
    string Id,
    string Patient,
    string PatientName,
    string Text,
    string Category,
    bool Critical,
    Grading Grading,
    string? Reference = null)
{
    public int Weight => Critical ? 3 : 1;
}

public static class QuestionSet
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private sealed record File(string? Description, IReadOnlyList<QuestionDto> Questions);

    private sealed record QuestionDto(
        string Id, string Patient, string PatientName,
        [property: JsonPropertyName("question")] string Text,
        string Category, bool Critical, Grading Grading, string? Reference);

    public static IReadOnlyList<Question> Parse(string json)
    {
        var file = JsonSerializer.Deserialize<File>(json, Options)
            ?? throw new InvalidDataException("The questions file is empty.");

        var questions = file.Questions
            .Select(q => new Question(q.Id, q.Patient, q.PatientName, q.Text, q.Category, q.Critical, q.Grading, q.Reference))
            .ToList();

        var duplicate = questions.GroupBy(q => q.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Question id \"{duplicate.Key}\" appears more than once.");
        }
        foreach (var q in questions)
        {
            var g = q.Grading;
            if (g.StartsWith is null && g.All is null && g.None is null && g.Numbers is null)
            {
                throw new InvalidDataException($"Question \"{q.Id}\" has no grading rules.");
            }
            if (g.StartsWith is not null and not "yes" and not "no")
            {
                throw new InvalidDataException($"Question \"{q.Id}\": startsWith must be \"yes\" or \"no\".");
            }
        }
        return questions;
    }

    public static IReadOnlyList<Question> Load(string path) => Parse(System.IO.File.ReadAllText(path));
}
