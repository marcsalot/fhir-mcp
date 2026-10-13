using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FhirMcp.Eval;

/// <summary>A tool the model may call, as the MCP server describes it.</summary>
public sealed record ToolDefinition(string Name, string Description, JsonElement InputSchema);

public sealed record ToolOutcome(string Text, bool IsError);

/// <summary>Runs tool calls for the fetch mode (the MCP server, or a fake in tests).</summary>
public interface IToolExecutor
{
    IReadOnlyList<ToolDefinition> Tools { get; }

    Task<ToolOutcome> CallAsync(string name, JsonObject arguments, CancellationToken cancellationToken);
}

public sealed record ToolCallRecord(string Name, string Arguments, bool IsError);

/// <param name="Model">The model version that answered, as reported by the API when it says.</param>
/// <param name="Error">Set when no answer was produced (API error, too many tool calls).</param>
public sealed record AgentResult(
    string Answer,
    IReadOnlyList<ToolCallRecord> ToolCalls,
    TimeSpan Elapsed,
    int? InputTokens,
    int? OutputTokens,
    string Model,
    string? Error = null);

/// <summary>A model under evaluation: answers a prompt, using tools when given them.</summary>
public interface IAgent
{
    /// <summary>The label used in reports, such as "claude-sonnet-5-5" or "medgemma:27b".</summary>
    string Name { get; }

    Task<AgentResult> RunAsync(string system, string prompt, IToolExecutor? tools, CancellationToken cancellationToken);
}

internal static class AgentLimits
{
    /// <summary>Tool calls allowed for one question before the attempt is stopped.</summary>
    public const int MaxToolCalls = 10;
}

/// <summary>
/// Claude through the Anthropic Messages API, using its native tool use in the fetch mode.
/// Reads the key from ANTHROPIC_API_KEY; the key is sent only to api.anthropic.com.
/// </summary>
public sealed class ClaudeAgent : IAgent
{
    public const string DefaultModel = "claude-sonnet-5-5";
    private const string Endpoint = "https://api.anthropic.com/v1/messages";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public ClaudeAgent(HttpClient http, string apiKey, string model = DefaultModel)
    {
        _http = http;
        _apiKey = apiKey;
        _model = model;
    }

    public string Name => _model;

    public async Task<AgentResult> RunAsync(string system, string prompt, IToolExecutor? tools, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var calls = new List<ToolCallRecord>();
        var messages = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = prompt } };
        int inputTokens = 0, outputTokens = 0;
        var model = _model;

        while (true)
        {
            var request = new JsonObject
            {
                ["model"] = _model,
                ["max_tokens"] = 1024,
                ["temperature"] = 0,
                ["system"] = system,
                ["messages"] = messages.DeepClone(),
            };
            if (tools is not null)
            {
                request["tools"] = new JsonArray(tools.Tools.Select(t => (JsonNode)new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = JsonNode.Parse(t.InputSchema.GetRawText()),
                }).ToArray());
            }

            JsonNode response;
            try
            {
                response = await PostAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                return new AgentResult("", calls, clock.Elapsed, inputTokens, outputTokens, model, ex.Message);
            }

            model = response["model"]?.GetValue<string>() ?? model;
            inputTokens += response["usage"]?["input_tokens"]?.GetValue<int>() ?? 0;
            outputTokens += response["usage"]?["output_tokens"]?.GetValue<int>() ?? 0;

            var content = response["content"]?.AsArray() ?? new JsonArray();
            var toolUses = content.Where(c => c?["type"]?.GetValue<string>() == "tool_use").ToList();
            var text = string.Join("\n", content
                .Where(c => c?["type"]?.GetValue<string>() == "text")
                .Select(c => c!["text"]!.GetValue<string>())).Trim();

            if (toolUses.Count == 0 || tools is null)
            {
                return new AgentResult(text, calls, clock.Elapsed, inputTokens, outputTokens, model);
            }

            if (calls.Count + toolUses.Count > AgentLimits.MaxToolCalls)
            {
                return new AgentResult(text, calls, clock.Elapsed, inputTokens, outputTokens, model,
                    $"stopped after {AgentLimits.MaxToolCalls} tool calls");
            }

            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content.DeepClone() });
            var results = new JsonArray();
            foreach (var use in toolUses)
            {
                var name = use!["name"]!.GetValue<string>();
                var input = use["input"] as JsonObject ?? new JsonObject();
                var outcome = await tools.CallAsync(name, input, cancellationToken);
                calls.Add(new ToolCallRecord(name, input.ToJsonString(), outcome.IsError));
                results.Add(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = use["id"]!.GetValue<string>(),
                    ["content"] = outcome.Text,
                    ["is_error"] = outcome.IsError,
                });
            }
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = results });
        }
    }

    /// <summary>Posts one request, retrying rate limits and overloads with a growing pause.</summary>
    private async Task<JsonNode> PostAsync(JsonObject body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            using var response = await _http.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return JsonNode.Parse(text)!;
            }

            var retryable = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or (HttpStatusCode)529;
            if (!retryable || attempt == 4)
            {
                var message = JsonNode.Parse(text)?["error"]?["message"]?.GetValue<string>() ?? text;
                throw new HttpRequestException($"Anthropic API returned {(int)response.StatusCode}: {message}");
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2), cancellationToken);
        }
    }
}

/// <summary>
/// A local model served by Ollama (http://localhost:11434 by default). Small open models are not
/// reliable with native function calling, and Ollama does not offer it for every model, so the
/// fetch mode uses a plain text protocol described in the prompt: the model replies with a JSON
/// object to call a tool, and with "ANSWER:" when it is done. See <see cref="TextToolProtocol"/>.
/// </summary>
public sealed class OllamaAgent : IAgent
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly Uri _host;

    public OllamaAgent(HttpClient http, string model, Uri? host = null)
    {
        _http = http;
        _model = model;
        _host = host ?? new Uri("http://localhost:11434/");
    }

    public string Name => _model;

    public async Task<AgentResult> RunAsync(string system, string prompt, IToolExecutor? tools, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var calls = new List<ToolCallRecord>();
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = tools is null ? system : system + "\n\n" + TextToolProtocol.Instructions(tools.Tools) },
            new JsonObject { ["role"] = "user", ["content"] = prompt },
        };
        int inputTokens = 0, outputTokens = 0;

        while (true)
        {
            var request = new JsonObject
            {
                ["model"] = _model,
                ["messages"] = messages.DeepClone(),
                ["stream"] = false,
                ["keep_alive"] = "30m",
                // A fixed seed and no sampling make runs repeatable; the context must hold a whole chart.
                ["options"] = new JsonObject { ["temperature"] = 0, ["seed"] = 1, ["num_ctx"] = 16384 },
            };

            JsonNode response;
            try
            {
                using var http = await _http.PostAsync(new Uri(_host, "api/chat"),
                    new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"), cancellationToken);
                var body = await http.Content.ReadAsStringAsync(cancellationToken);
                if (!http.IsSuccessStatusCode)
                {
                    var message = JsonNode.Parse(body)?["error"]?.GetValue<string>() ?? body;
                    throw new HttpRequestException($"Ollama returned {(int)http.StatusCode}: {message}");
                }
                response = JsonNode.Parse(body)!;
            }
            catch (HttpRequestException ex)
            {
                return new AgentResult("", calls, clock.Elapsed, inputTokens, outputTokens, _model, ex.Message);
            }

            inputTokens += response["prompt_eval_count"]?.GetValue<int>() ?? 0;
            outputTokens += response["eval_count"]?.GetValue<int>() ?? 0;
            var reply = TextToolProtocol.StripThinking(response["message"]?["content"]?.GetValue<string>() ?? "");

            if (tools is null)
            {
                return new AgentResult(reply.Trim(), calls, clock.Elapsed, inputTokens, outputTokens, _model);
            }

            var step = TextToolProtocol.Parse(reply);
            if (step.ToolName is null)
            {
                return new AgentResult(step.Answer, calls, clock.Elapsed, inputTokens, outputTokens, _model);
            }
            if (calls.Count == AgentLimits.MaxToolCalls)
            {
                return new AgentResult("", calls, clock.Elapsed, inputTokens, outputTokens, _model,
                    $"stopped after {AgentLimits.MaxToolCalls} tool calls");
            }

            var outcome = tools.Tools.Any(t => t.Name == step.ToolName)
                ? await tools.CallAsync(step.ToolName, step.Arguments!, cancellationToken)
                : new ToolOutcome($"There is no tool named \"{step.ToolName}\". Use one of: {string.Join(", ", tools.Tools.Select(t => t.Name))}.", true);
            calls.Add(new ToolCallRecord(step.ToolName, step.Arguments!.ToJsonString(), outcome.IsError));

            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = reply });
            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = $"Result of {step.ToolName}{(outcome.IsError ? " (error)" : "")}:\n{outcome.Text}\n\n" +
                              "Call another tool if you need more, or reply with ANSWER: followed by your answer.",
            });
        }
    }

    /// <summary>The digest of an installed model, so a report records exactly which build was tested.</summary>
    public async Task<string?> DescribeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tags = await _http.GetFromJsonAsync<JsonNode>(new Uri(_host, "api/tags"), cancellationToken);
            var entry = tags?["models"]?.AsArray().FirstOrDefault(m =>
                m?["name"]?.GetValue<string>() == _model || m?["name"]?.GetValue<string>() == _model + ":latest");
            var digest = entry?["digest"]?.GetValue<string>();
            var size = entry?["details"]?["parameter_size"]?.GetValue<string>();
            var quant = entry?["details"]?["quantization_level"]?.GetValue<string>();
            return digest is null ? null : $"{size} {quant}, digest {digest[..12]}".Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }
}

/// <summary>
/// The text protocol for models without dependable native tool calling: tool calls are a JSON
/// object on their own, final answers start with "ANSWER:".
/// </summary>
public static partial class TextToolProtocol
{
    public sealed record Step(string? ToolName, JsonObject? Arguments, string Answer);

    public static string Instructions(IReadOnlyList<ToolDefinition> tools)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You can call these tools to read the patient's record:");
        foreach (var tool in tools)
        {
            sb.AppendLine();
            sb.AppendLine($"- {tool.Name}: {tool.Description}");
            sb.AppendLine($"  Arguments (JSON schema): {tool.InputSchema.GetRawText()}");
        }
        sb.AppendLine();
        sb.AppendLine("To call a tool, reply with only a JSON object and nothing else, for example:");
        sb.AppendLine("{\"tool\": \"search_patients\", \"arguments\": {\"name\": \"Jane Doe\"}}");
        sb.AppendLine("Call one tool at a time. You will receive its result in the next message.");
        sb.AppendLine("When you have what you need, reply with ANSWER: followed by your answer.");
        return sb.ToString();
    }

    public static Step Parse(string reply)
    {
        var text = reply.Trim();
        var answerAt = text.IndexOf("ANSWER:", StringComparison.OrdinalIgnoreCase);

        // A tool call is a JSON object with a "tool" name, possibly inside a code fence.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start && (answerAt < 0 || start < answerAt))
        {
            try
            {
                if (JsonNode.Parse(text[start..(end + 1)]) is JsonObject call
                    && call["tool"]?.GetValue<string>() is { Length: > 0 } name)
                {
                    return new Step(name, call["arguments"] as JsonObject ?? new JsonObject(), "");
                }
            }
            catch (JsonException)
            {
                // Not a tool call; treat it as an answer below.
            }
        }

        return new Step(null, null, answerAt >= 0 ? text[(answerAt + "ANSWER:".Length)..].Trim() : text);
    }

    /// <summary>Removes reasoning that some models emit before the answer, such as MedGemma's thought markers.</summary>
    public static string StripThinking(string reply) =>
        ThoughtBlock().Replace(reply, "").Trim();

    [GeneratedRegex(@"<unused94>.*?<unused95>|<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThoughtBlock();
}
