using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FhirMcp.Core.Privacy;
using FhirMcp.Eval;
using Xunit;

namespace FhirMcp.Tests;

internal static class EvalFiles
{
    public static string Root { get; } = FindRoot();

    public static IReadOnlyList<Question> Questions { get; } = QuestionSet.Load(Path.Combine(Root, "eval", "questions.json"));

    public static PatientChart Chart(string patient) =>
        PatientChart.Parse(File.ReadAllText(Path.Combine(Root, "eval", "context", patient + ".json")));

    public static Question Question(string id) => Questions.Single(q => q.Id == id);

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FhirMcp.sln"))) return dir.FullName;
        }
        throw new InvalidOperationException("Repo root not found.");
    }
}

public class EvalQuestionSetTests
{
    [Fact]
    public void Has_fifty_questions_across_five_patients_with_charts_for_each()
    {
        Assert.Equal(50, EvalFiles.Questions.Count);
        Assert.Equal(5, EvalFiles.Questions.Select(q => q.Patient).Distinct().Count());
        Assert.All(EvalFiles.Questions.Select(q => q.Patient).Distinct(), p => Assert.NotNull(EvalFiles.Chart(p)));
    }

    /// <summary>
    /// Proves every question can be passed: a correct answer written from the frozen record
    /// passes its own grading. Guards against rules that are wrong or unsatisfiable.
    /// </summary>
    [Fact]
    public void Every_reference_answer_passes_its_own_grading()
    {
        var failures = EvalFiles.Questions
            .Select(q => (q, Verdict: Grader.Grade(q, q.Reference!, EvalFiles.Chart(q.Patient).MedicationText)))
            .Where(x => !x.Verdict.Passed)
            .Select(x => $"{x.q.Id}: {string.Join("; ", x.Verdict.Problems)}")
            .ToList();

        Assert.Empty(failures);
    }

    [Fact]
    public void Refuses_duplicate_ids_and_questions_without_rules()
    {
        Assert.Throws<InvalidDataException>(() => QuestionSet.Parse("""
            { "questions": [
              { "id": "a", "patient": "p", "patientName": "P", "question": "?", "category": "c", "critical": false, "grading": { "startsWith": "yes" } },
              { "id": "a", "patient": "p", "patientName": "P", "question": "?", "category": "c", "critical": false, "grading": { "startsWith": "no" } } ] }
            """));
        Assert.Throws<InvalidDataException>(() => QuestionSet.Parse("""
            { "questions": [ { "id": "a", "patient": "p", "patientName": "P", "question": "?", "category": "c", "critical": false, "grading": {} } ] }
            """));
    }
}

public class GraderTests
{
    private static Verdict Grade(string id, string answer) =>
        Grader.Grade(EvalFiles.Question(id), answer, EvalFiles.Chart(EvalFiles.Question(id).Patient).MedicationText);

    [Theory]
    [InlineData("**No.** She is not on metformin.", "no")]
    [InlineData("- Yes, she is.", "yes")]
    [InlineData("## Answer\nNo", "answer")]
    public void Reads_the_first_word_past_markdown(string answer, string expected) =>
        Assert.Equal(expected, Grader.FirstWord(Grader.Normalize(answer)));

    [Fact]
    public void Finds_every_number_including_blood_pressure_pairs_and_thousands()
    {
        Assert.Equal(new[] { 154.4, 90.46, 1234.5 }, Grader.NumbersIn("154.4/90.46 mm[hg], then 1,234.5"));
    }

    [Fact]
    public void A_wrong_yes_no_answer_fails()
    {
        var verdict = Grade("reyna-metformin", "Yes, she takes metformin 500 mg twice daily.");
        Assert.False(verdict.Passed);
        Assert.Contains(verdict.Problems, p => p.Contains("start with \"no\""));
    }

    [Fact]
    public void A_missing_medication_is_named()
    {
        var verdict = Grade("reyna-active-meds", "She takes hydrochlorothiazide, ferrous sulfate, fluticasone and diphenhydramine.");
        Assert.False(verdict.Passed);
        Assert.Equal("missing: \"albuterol\"", Assert.Single(verdict.Problems));
    }

    [Fact]
    public void An_invented_medication_fails_but_a_negated_one_does_not()
    {
        var invented = Grade("reyna-bp-med", "Yes. She takes hydrochlorothiazide and lisinopril.");
        Assert.False(invented.Passed);
        Assert.Equal(new[] { "lisinopril" }, invented.InventedMedications);

        var negated = Grade("reyna-bp-med", "Yes. She takes hydrochlorothiazide. She is not on lisinopril.");
        Assert.True(negated.Passed, string.Join("; ", negated.Problems));
    }

    [Fact]
    public void Drugs_named_in_the_question_are_not_counted_as_invented()
    {
        Assert.Empty(Grade("reyna-metformin", "No, metformin is not listed, and metformin does not appear in her orders.").InventedMedications);
    }

    [Theory]
    [InlineData("Her last visit was on May 27, 2020, for an asthma follow-up.", true)]
    [InlineData("Her last visit was on 5/27/2020 for asthma.", true)]
    [InlineData("Her last visit was in 2020, for asthma.", false)]
    public void Full_dates_are_accepted_in_common_formats_but_a_year_is_not(string answer, bool passes) =>
        Assert.Equal(passes, Grade("reyna-last-visit", answer).Passed);

    [Theory]
    [InlineData("Her most recent weight is 77.85 kg.", true)]
    [InlineData("Her most recent weight is about 171.6 lb.", true)]
    [InlineData("Her most recent weight is 82 kg.", false)]
    public void Numbers_match_within_tolerance_in_either_unit(string answer, bool passes) =>
        Assert.Equal(passes, Grade("reyna-weight", answer).Passed);

    [Fact]
    public void Inventing_a_value_that_is_not_on_file_fails()
    {
        Assert.True(Grade("mathilda-a1c", "There is no A1c result on file for her.").Passed);
        Assert.False(Grade("mathilda-a1c", "Her most recent A1c was 5.4%.").Passed);
    }
}

public class PatientChartTests
{
    [Fact]
    public void De_identified_chart_has_no_name_city_or_full_dates()
    {
        var chart = EvalFiles.Chart("reyna-ziemann");

        var full = chart.Render(PrivacyMode.Full);
        Assert.Contains("Reyna Ziemann", full);
        Assert.Contains("2020-05-27", full);

        var deidentified = chart.Render(PrivacyMode.Deidentified);
        Assert.DoesNotContain("Ziemann", deidentified);
        Assert.DoesNotContain("Raynham", deidentified);
        Assert.DoesNotContain("2020-05-27", deidentified);
        Assert.Contains("hydrochlorothiazide", deidentified, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Medication_text_lists_past_and_present_orders()
    {
        var text = EvalFiles.Chart("adolph-smith").MedicationText;
        Assert.Contains("Ibuprofen", text);
        Assert.Contains("Naproxen", text);
    }
}

public class TextToolProtocolTests
{
    [Fact]
    public void Reads_a_tool_call_even_inside_a_code_fence()
    {
        var step = TextToolProtocol.Parse("""
            ```json
            {"tool": "search_patients", "arguments": {"name": "Reyna Ziemann"}}
            ```
            """);
        Assert.Equal("search_patients", step.ToolName);
        Assert.Equal("Reyna Ziemann", step.Arguments!["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("ANSWER: No, she is not on metformin.", "No, she is not on metformin.")]
    [InlineData("She has no allergies on file.", "She has no allergies on file.")]
    [InlineData("I checked the record. ANSWER: Yes, she has asthma {see conditions}.", "Yes, she has asthma {see conditions}.")]
    public void Reads_a_final_answer(string reply, string expected)
    {
        var step = TextToolProtocol.Parse(reply);
        Assert.Null(step.ToolName);
        Assert.Equal(expected, step.Answer);
    }

    [Fact]
    public void Removes_reasoning_blocks()
    {
        Assert.Equal("Yes.", TextToolProtocol.StripThinking("<unused94>thought: check meds<unused95>Yes."));
        Assert.Equal("No.", TextToolProtocol.StripThinking("<think>hmm</think>No."));
    }
}

internal sealed class FakeTools : IToolExecutor
{
    public List<(string Name, JsonObject Arguments)> Calls { get; } = new();

    public IReadOnlyList<ToolDefinition> Tools { get; } =
    [
        new("search_patients", "Find patients", JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}}}""").RootElement),
        new("get_medications", "Medications", JsonDocument.Parse("""{"type":"object","properties":{"patientId":{"type":"string"}}}""").RootElement),
    ];

    public Task<ToolOutcome> CallAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        Calls.Add((name, arguments));
        return Task.FromResult(name == "search_patients"
            ? new ToolOutcome("""{"patients":[{"id":"p-1","name":"Reyna Ziemann"}]}""", false)
            : new ToolOutcome("""{"items":[{"name":"Hydrochlorothiazide 25 MG","status":"active"}]}""", false));
    }
}

public class AgentTests
{
    [Fact]
    public async Task Claude_agent_runs_tool_calls_and_returns_the_final_text()
    {
        var turn = 0;
        var server = new FakeServer((_, _, _) => (HttpStatusCode.OK, ++turn switch
        {
            1 => """{"model":"claude-test-1","stop_reason":"tool_use","usage":{"input_tokens":100,"output_tokens":10},"content":[{"type":"tool_use","id":"t1","name":"search_patients","input":{"name":"Reyna Ziemann"}}]}""",
            2 => """{"model":"claude-test-1","stop_reason":"tool_use","usage":{"input_tokens":150,"output_tokens":10},"content":[{"type":"text","text":"Checking."},{"type":"tool_use","id":"t2","name":"get_medications","input":{"patientId":"p-1"}}]}""",
            _ => """{"model":"claude-test-1","stop_reason":"end_turn","usage":{"input_tokens":200,"output_tokens":20},"content":[{"type":"text","text":"Yes. She takes hydrochlorothiazide."}]}""",
        }));
        var tools = new FakeTools();
        var agent = new ClaudeAgent(new HttpClient(server), "test-key", "claude-test");

        var result = await agent.RunAsync("system", "Is she on a blood pressure medicine?", tools, CancellationToken.None);

        Assert.Equal("Yes. She takes hydrochlorothiazide.", result.Answer);
        Assert.Equal(new[] { "search_patients", "get_medications" }, result.ToolCalls.Select(c => c.Name));
        Assert.Equal((450, 40), (result.InputTokens, result.OutputTokens));
        Assert.Equal("claude-test-1", result.Model);

        Assert.All(server.Requests, r => Assert.Equal("api.anthropic.com", r.Url.Host));
        var last = JsonNode.Parse(server.Requests[^1].Body)!;
        Assert.Equal(0, last["temperature"]!.GetValue<int>());
        Assert.Equal(2, last["tools"]!.AsArray().Count);
        var toolResult = last["messages"]!.AsArray()[^1]!["content"]![0]!;
        Assert.Equal("tool_result", toolResult["type"]!.GetValue<string>());
        Assert.Equal("t2", toolResult["tool_use_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Claude_agent_reports_api_errors_as_no_answer()
    {
        var server = new FakeServer((_, _, _) => (HttpStatusCode.Unauthorized, """{"error":{"type":"authentication_error","message":"invalid x-api-key"}}"""));
        var result = await new ClaudeAgent(new HttpClient(server), "bad").RunAsync("s", "q", null, CancellationToken.None);

        Assert.Equal("", result.Answer);
        Assert.Contains("invalid x-api-key", result.Error);
    }

    [Fact]
    public async Task Ollama_agent_follows_the_text_protocol_with_repeatable_settings()
    {
        var turn = 0;
        var server = new FakeServer((_, _, _) => (HttpStatusCode.OK, ++turn switch
        {
            1 => """{"message":{"role":"assistant","content":"{\"tool\": \"search_patients\", \"arguments\": {\"name\": \"Reyna Ziemann\"}}"},"prompt_eval_count":900,"eval_count":20}""",
            _ => """{"message":{"role":"assistant","content":"ANSWER: Yes. She takes hydrochlorothiazide."},"prompt_eval_count":1000,"eval_count":12}""",
        }));
        var tools = new FakeTools();
        var agent = new OllamaAgent(new HttpClient(server), "medgemma:27b");

        var result = await agent.RunAsync("system", "Is she on a blood pressure medicine?", tools, CancellationToken.None);

        Assert.Equal("Yes. She takes hydrochlorothiazide.", result.Answer);
        Assert.Equal("search_patients", Assert.Single(tools.Calls).Name);
        var first = JsonNode.Parse(server.Requests[0].Body)!;
        Assert.Equal("http://localhost:11434/api/chat", server.Requests[0].Url.AbsoluteUri);
        Assert.Equal(0, first["options"]!["temperature"]!.GetValue<int>());
        Assert.Equal(16384, first["options"]!["num_ctx"]!.GetValue<int>());
        Assert.Contains("ANSWER:", first["messages"]![0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Ollama_agent_stops_a_model_that_never_answers()
    {
        var server = new FakeServer((_, _, _) => (HttpStatusCode.OK,
            """{"message":{"role":"assistant","content":"{\"tool\": \"search_patients\", \"arguments\": {}}"}}"""));
        var result = await new OllamaAgent(new HttpClient(server), "m").RunAsync("s", "q", new FakeTools(), CancellationToken.None);

        Assert.Contains("stopped after", result.Error);
    }
}

public class RunnerAndReportTests
{
    private sealed class ScriptedAgent(string name, Func<string, string> answer) : IAgent
    {
        public List<string> Prompts { get; } = new();

        public string Name => name;

        public Task<AgentResult> RunAsync(string system, string prompt, IToolExecutor? tools, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            return Task.FromResult(new AgentResult(answer(prompt), [], TimeSpan.FromSeconds(1), 10, 5, name + "-v1"));
        }
    }

    [Fact]
    public async Task Runs_every_question_per_model_and_privacy_mode_and_reports_failures()
    {
        var questions = new[] { EvalFiles.Question("reyna-metformin"), EvalFiles.Question("reyna-last-visit") };
        var charts = new Dictionary<string, PatientChart> { ["reyna-ziemann"] = EvalFiles.Chart("reyna-ziemann") };
        var good = new ScriptedAgent("good", p => p.Contains("metformin") ? "No." : "On 2020-05-27, an asthma follow-up.");
        var bad = new ScriptedAgent("bad", _ => "Yes.");
        var collected = new List<QuestionResult>();

        var results = await new Runner(questions, charts, null, _ => { })
            .RunAsync([(good, 1), (bad, 2)], [EvalMode.Answer], [PrivacyMode.Full, PrivacyMode.Deidentified], collected.Add, CancellationToken.None);

        Assert.Equal(8, results.Count);
        Assert.Equal(8, collected.Count);
        Assert.Contains(good.Prompts, p => p.Contains("Raynham"));
        Assert.Contains(good.Prompts, p => !p.Contains("Raynham")); // the de-identified chart has no city

        var summaries = Report.Summarize(results).ToDictionary(s => s.Label);
        Assert.Equal(100, summaries["good / answer / full"].Score);
        Assert.Equal(0, summaries["bad / answer / full"].Score);
        Assert.Equal(1, summaries["bad / answer / full"].CriticalMissed);

        var report = Report.Build(results, questions, new Dictionary<string, string> { ["good"] = "test" }, "run --test", DateTimeOffset.UnixEpoch);
        Assert.Contains("| good / answer / full | 100.0% | 2/2 |", report);
        Assert.Contains("### reyna-metformin (critical)", report);
        Assert.Contains("**bad / answer / full**", report);
        Assert.DoesNotContain("—", report);
    }
}
