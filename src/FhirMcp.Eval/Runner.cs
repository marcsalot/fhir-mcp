using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FhirMcp.Core.Privacy;

namespace FhirMcp.Eval;

/// <summary>How the model gets the patient's record.</summary>
public enum EvalMode
{
    /// <summary>The frozen chart from eval/context is in the prompt; this measures reading and reasoning.</summary>
    Answer,

    /// <summary>The model gets only the question and calls the live MCP server's tools itself.</summary>
    Fetch,
}

public sealed record QuestionResult(
    string Model,
    string ModelVersion,
    EvalMode Mode,
    PrivacyMode Privacy,
    string QuestionId,
    string Category,
    bool Critical,
    int Weight,
    bool Passed,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> InventedMedications,
    string Answer,
    IReadOnlyList<ToolCallRecord> ToolCalls,
    double Seconds,
    int? InputTokens,
    int? OutputTokens,
    string? Error)
{
    public string RunLabel => $"{Model} / {Mode.ToString().ToLowerInvariant()} / {Privacy.ToString().ToLowerInvariant()}";
}

/// <summary>A patient's frozen record: the tool outputs used as context in the answer mode.</summary>
public sealed class PatientChart
{
    private readonly IReadOnlyList<(string Call, string Json)> _tools;

    private PatientChart(IReadOnlyList<(string, string)> tools, string medicationText)
    {
        _tools = tools;
        MedicationText = medicationText;
    }

    /// <summary>Every medication name on record, past and present, for the invented-medication check.</summary>
    public string MedicationText { get; }

    public static PatientChart Parse(string json)
    {
        var root = JsonNode.Parse(json)!;
        var tools = root["tools"]!.AsObject()
            .Select(kv => (kv.Key, kv.Value!.ToJsonString()))
            .ToList();
        var meds = root["tools"]?["get_medications status=all"]?["items"]?.AsArray()
            .Select(m => m?["name"]?.GetValue<string>())
            .OfType<string>() ?? [];
        return new PatientChart(tools, string.Join("\n", meds));
    }

    /// <summary>The chart as the model sees it, de-identified by the server's own rules when asked.</summary>
    public string Render(PrivacyMode privacy)
    {
        var sb = new StringBuilder();
        foreach (var (call, json) in _tools)
        {
            sb.AppendLine($"### {call}");
            sb.AppendLine(privacy == PrivacyMode.Deidentified ? Deidentifier.Apply(json) : json);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

public static class Prompts
{
    public const string System =
        "You answer questions about one patient's record from a FHIR server. The patients are synthetic test data. " +
        "Answer only from the record: do not guess, assume, or use general medical knowledge to fill gaps. " +
        "If the record does not contain the answer, say so. " +
        "If the question can be answered yes or no, begin your answer with Yes or No. " +
        "Keep the answer to two or three sentences, and give numbers with their units and dates as recorded.";

    public const string FetchAddition =
        "Use the tools to find the patient by name and read the parts of the record you need before answering.";

    public static string AnswerPrompt(string chart, string question) =>
        $"Here is the patient's record, as returned by the FHIR server's tools:\n\n{chart}\nQuestion: {question}";
}

public sealed class Runner
{
    private readonly IReadOnlyList<Question> _questions;
    private readonly IReadOnlyDictionary<string, PatientChart> _charts;
    private readonly Func<PrivacyMode, CancellationToken, Task<IToolExecutor>>? _startTools;
    private readonly Action<string> _log;

    public Runner(
        IReadOnlyList<Question> questions,
        IReadOnlyDictionary<string, PatientChart> charts,
        Func<PrivacyMode, CancellationToken, Task<IToolExecutor>>? startTools,
        Action<string> log)
    {
        _questions = questions;
        _charts = charts;
        _startTools = startTools;
        _log = log;
    }

    /// <param name="onResult">Called after each question, so partial runs are kept.</param>
    public async Task<IReadOnlyList<QuestionResult>> RunAsync(
        IReadOnlyList<(IAgent Agent, int Concurrency)> agents,
        IReadOnlyList<EvalMode> modes,
        IReadOnlyList<PrivacyMode> privacyModes,
        Action<QuestionResult> onResult,
        CancellationToken cancellationToken)
    {
        var results = new List<QuestionResult>();
        var tools = new Dictionary<PrivacyMode, IToolExecutor>();
        try
        {
            foreach (var mode in modes)
            {
                foreach (var privacy in privacyModes)
                {
                    IToolExecutor? executor = null;
                    if (mode == EvalMode.Fetch)
                    {
                        if (_startTools is null) throw new InvalidOperationException("The fetch mode needs the MCP server.");
                        if (!tools.TryGetValue(privacy, out executor))
                        {
                            executor = tools[privacy] = await _startTools(privacy, cancellationToken);
                        }
                    }

                    foreach (var (agent, concurrency) in agents)
                    {
                        var label = $"{agent.Name} / {mode.ToString().ToLowerInvariant()} / {privacy.ToString().ToLowerInvariant()}";
                        _log($"\n{label}: {_questions.Count} questions");
                        var gate = new SemaphoreSlim(concurrency);
                        var done = 0;
                        var passed = 0;
                        var lockObj = new object();

                        // Questions run in file order (grouped by patient), which lets a local model
                        // reuse the cached chart between questions about the same patient.
                        var tasks = _questions.Select(async question =>
                        {
                            await gate.WaitAsync(cancellationToken);
                            try
                            {
                                var result = await AskAsync(agent, question, mode, privacy, executor, cancellationToken);
                                lock (lockObj)
                                {
                                    results.Add(result);
                                    onResult(result);
                                    done++;
                                    if (result.Passed) passed++;
                                    _log($"  [{done,2}/{_questions.Count}] {(result.Passed ? "pass" : "FAIL")} {question.Id} ({result.Seconds:0.0}s)" +
                                         (result.Error is null ? "" : $" error: {result.Error}"));
                                }
                            }
                            finally
                            {
                                gate.Release();
                            }
                        }).ToList();
                        await Task.WhenAll(tasks);
                        _log($"  {label}: {passed}/{_questions.Count} passed");
                    }
                }
            }
        }
        finally
        {
            foreach (var executor in tools.Values)
            {
                if (executor is IAsyncDisposable d) await d.DisposeAsync();
            }
        }
        return results;
    }

    private async Task<QuestionResult> AskAsync(
        IAgent agent, Question question, EvalMode mode, PrivacyMode privacy, IToolExecutor? tools, CancellationToken cancellationToken)
    {
        var chart = _charts[question.Patient];
        var clock = Stopwatch.StartNew();
        var result = mode == EvalMode.Answer
            ? await agent.RunAsync(Prompts.System, Prompts.AnswerPrompt(chart.Render(privacy), question.Text), null, cancellationToken)
            : await agent.RunAsync(Prompts.System + " " + Prompts.FetchAddition, question.Text, tools, cancellationToken);

        var verdict = result.Error is null
            ? Grader.Grade(question, result.Answer, chart.MedicationText)
            : new Verdict(false, [$"no answer: {result.Error}"], []);

        return new QuestionResult(
            agent.Name, result.Model, mode, privacy, question.Id, question.Category, question.Critical, question.Weight,
            verdict.Passed, verdict.Problems, verdict.InventedMedications, result.Answer, result.ToolCalls,
            Math.Round(clock.Elapsed.TotalSeconds, 1), result.InputTokens, result.OutputTokens, result.Error);
    }

    public static readonly JsonSerializerOptions JsonLineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
