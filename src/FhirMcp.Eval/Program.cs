using System.Globalization;
using System.Text.Json;
using FhirMcp.Core.Privacy;
using FhirMcp.Eval;

// Evaluates how well models answer clinical questions over this project's FHIR tools.
//
//   dotnet run --project src/FhirMcp.Eval -- run                      all models, answer mode, both privacy modes
//   dotnet run --project src/FhirMcp.Eval -- run --models claude --modes answer,fetch --privacy full
//   dotnet run --project src/FhirMcp.Eval -- run --only reyna-active-meds,adolph-seizure-med
//   dotnet run --project src/FhirMcp.Eval -- snapshot                 refreeze patient charts from the sandbox
//
// Models: "claude" (ANTHROPIC_API_KEY, model claude-sonnet-5-5) or "claude:<model id>", and
// any Ollama model by name ("medgemma:27b", "medgemma1.5:4b"); OLLAMA_HOST overrides
// http://localhost:11434. Results go to eval/results/<time>/ as report.md and results.jsonl.

var root = FindRepoRoot();
if (root is null)
{
    Console.Error.WriteLine("Run this from inside the fhir-mcp folder (FhirMcp.sln was not found).");
    return 2;
}

var command = args.FirstOrDefault();
var options = ParseOptions(args.Skip(1).ToArray());
if (options is null || command is not ("run" or "snapshot"))
{
    Console.Error.WriteLine("Usage: run [--models claude,medgemma:27b,medgemma1.5:4b] [--modes answer,fetch] [--privacy full,deidentified]");
    Console.Error.WriteLine("           [--only id1,id2] [--limit N] [--server path/to/FhirMcp.Server] [--out folder]");
    Console.Error.WriteLine("       snapshot");
    return 2;
}

var questionsPath = Path.Combine(root, "eval", "questions.json");
var contextFolder = Path.Combine(root, "eval", "context");

if (command == "snapshot")
{
    var ids = QuestionSet.Load(questionsPath).Select(q => q.Patient).Distinct()
        .ToDictionary(p => p, p => JsonDocument.Parse(File.ReadAllText(Path.Combine(contextFolder, p + ".json"))).RootElement.GetProperty("patientId").GetString()!);
    return await Snapshot.RunAsync("https://r4.smarthealthit.org/", ids, contextFolder);
}

var questions = QuestionSet.Load(questionsPath).ToList();
if (options.TryGetValue("only", out var only))
{
    var wanted = only.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    var unknown = wanted.Where(w => questions.All(q => q.Id != w)).ToList();
    if (unknown.Count > 0)
    {
        Console.Error.WriteLine($"Unknown question ids: {string.Join(", ", unknown)}");
        return 2;
    }
    questions = questions.Where(q => wanted.Contains(q.Id)).ToList();
}
if (options.TryGetValue("limit", out var limitText) && int.TryParse(limitText, out var limit))
{
    questions = questions.Take(limit).ToList();
}

var charts = questions.Select(q => q.Patient).Distinct()
    .ToDictionary(p => p, p => PatientChart.Parse(File.ReadAllText(Path.Combine(contextFolder, p + ".json"))));

var modes = Split(options, "modes", "answer").Select(m => m switch
{
    "answer" => EvalMode.Answer,
    "fetch" => EvalMode.Fetch,
    _ => throw new ArgumentException($"Unknown mode \"{m}\". Use answer or fetch."),
}).ToList();
var privacy = Split(options, "privacy", "full,deidentified").Select(p => p switch
{
    "full" => PrivacyMode.Full,
    "deidentified" => PrivacyMode.Deidentified,
    _ => throw new ArgumentException($"Unknown privacy mode \"{p}\". Use full or deidentified."),
}).ToList();

// Local models can take minutes on long prompts; the API answers in seconds.
var ollamaHost = new Uri((Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434").TrimEnd('/') + "/");
var ollamaHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
var claudeHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
var agents = new List<(IAgent Agent, int Concurrency)>();
var notes = new Dictionary<string, string>();
foreach (var spec in Split(options, "models", "claude,medgemma:27b,medgemma1.5:4b"))
{
    if (spec == "claude" || spec.StartsWith("claude:", StringComparison.Ordinal))
    {
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("Set ANTHROPIC_API_KEY to evaluate Claude, or leave claude out of --models.");
            return 2;
        }
        var model = spec == "claude" ? ClaudeAgent.DefaultModel : spec["claude:".Length..];
        agents.Add((new ClaudeAgent(claudeHttp, key, model), 4));
        notes[model] = "Anthropic API, temperature 0, native tool use in the fetch mode";
    }
    else
    {
        var name = spec.StartsWith("ollama:", StringComparison.Ordinal) ? spec["ollama:".Length..] : spec;
        var agent = new OllamaAgent(ollamaHttp, name, ollamaHost);
        var description = await agent.DescribeAsync(CancellationToken.None);
        if (description is null)
        {
            Console.Error.WriteLine($"Ollama model \"{name}\" was not found at {ollamaHost}. Is Ollama running? Try: ollama pull {name}");
            return 2;
        }
        agents.Add((agent, 1));
        notes[name] = $"Ollama on this machine ({description}), temperature 0, text tool protocol in the fetch mode";
    }
}

string? serverPath = null;
if (modes.Contains(EvalMode.Fetch))
{
    serverPath = options.GetValueOrDefault("server") ?? FindServer(root);
    if (serverPath is null || !File.Exists(serverPath))
    {
        Console.Error.WriteLine("The fetch mode needs the built server. Run `dotnet build` first, or pass --server path/to/FhirMcp.Server.");
        return 2;
    }
}

var when = DateTimeOffset.UtcNow;
var outFolder = options.GetValueOrDefault("out") ?? Path.Combine(root, "eval", "results", when.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture));
Directory.CreateDirectory(outFolder);
var jsonlPath = Path.Combine(outFolder, "results.jsonl");
await using var jsonl = new StreamWriter(jsonlPath, append: false);

var runner = new Runner(questions, charts,
    serverPath is null ? null : async (p, ct) => await McpToolExecutor.StartAsync(serverPath, p, ct),
    Console.WriteLine);

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };

IReadOnlyList<QuestionResult> results;
try
{
    results = await runner.RunAsync(agents, modes, privacy,
        r => { jsonl.WriteLine(JsonSerializer.Serialize(r, Runner.JsonLineOptions)); jsonl.Flush(); },
        cancel.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine($"Stopped. Results so far are in {jsonlPath}.");
    return 1;
}

var commandLine = "run " + string.Join(' ', args.Skip(1));
var report = Report.Build(results, questions, notes, commandLine.Trim(), when);
var reportPath = Path.Combine(outFolder, "report.md");
await File.WriteAllTextAsync(reportPath, report);

Console.WriteLine();
foreach (var s in Report.Summarize(results))
{
    Console.WriteLine($"{s.Label,-50} {s.Score,5:0.0}%  {s.Passed}/{s.Questions} passed  {s.CriticalMissed} critical misses  {s.InventedMedications} invented");
}
Console.WriteLine($"\nReport: {reportPath}");
return 0;

static string? FindRepoRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "FhirMcp.sln"))) return dir.FullName;
    }
    return null;
}

static string? FindServer(string root) =>
    new[] { "Debug", "Release" }
        .Select(c => Path.Combine(root, "src", "FhirMcp.Server", "bin", c, "net10.0", OperatingSystem.IsWindows() ? "FhirMcp.Server.exe" : "FhirMcp.Server"))
        .Where(File.Exists)
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .FirstOrDefault();

static Dictionary<string, string>? ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>();
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) return null;
        options[args[i][2..]] = args[++i];
    }
    return options;
}

static IEnumerable<string> Split(Dictionary<string, string> options, string key, string fallback) =>
    options.GetValueOrDefault(key, fallback).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(s => s.ToLowerInvariant());
