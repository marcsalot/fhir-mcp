using System.Text.Json;
using System.Text.Json.Nodes;
using FhirMcp.Core.Privacy;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FhirMcp.Eval;

/// <summary>
/// The fetch mode's tools: the real fhir-mcp server, started as a separate process and spoken to
/// over MCP, exactly as Claude Desktop does. Every call goes through the server's own policy
/// filters and privacy output, so the de-identified runs test the real de-identification.
/// </summary>
public sealed class McpToolExecutor : IToolExecutor, IAsyncDisposable
{
    private readonly McpClient _client;

    private McpToolExecutor(McpClient client, IReadOnlyList<ToolDefinition> tools)
    {
        _client = client;
        Tools = tools;
    }

    public IReadOnlyList<ToolDefinition> Tools { get; }

    /// <param name="serverPath">The built FhirMcp.Server executable.</param>
    public static async Task<McpToolExecutor> StartAsync(string serverPath, PrivacyMode privacy, CancellationToken cancellationToken)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "fhir-mcp",
            Command = serverPath,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                // Always the open SMART sandbox, whatever this terminal has set, with no sign-in
                // and no audit records: evaluation traffic is not patient access.
                ["FHIR_MCP_SERVER"] = "smart",
                ["FHIR_BASE_URL"] = null,
                ["FHIR_MCP_AUTH"] = "none",
                ["FHIR_MCP_TOOLS"] = null,
                ["FHIR_MCP_AUDIT_LOG"] = "off",
                ["FHIR_MCP_PRIVACY"] = privacy == PrivacyMode.Deidentified ? "deidentified" : "full",
            },
        });

        var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "fhir-mcp-eval", Version = "1.0" },
        }, cancellationToken: cancellationToken);

        var listed = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var tools = listed
            .Select(t => new ToolDefinition(t.Name, t.Description ?? "", t.ProtocolTool.InputSchema))
            .ToList();
        return new McpToolExecutor(client, tools);
    }

    public async Task<ToolOutcome> CallAsync(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = arguments.ToDictionary(kv => kv.Key, kv => (object?)(kv.Value is null ? null : JsonSerializer.Deserialize<JsonElement>(kv.Value.ToJsonString())));
        try
        {
            var result = await _client.CallToolAsync(name, args, cancellationToken: cancellationToken);
            var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
            return new ToolOutcome(text, result.IsError == true);
        }
        catch (Exception ex) when (ex is ModelContextProtocol.McpException or JsonException)
        {
            return new ToolOutcome(ex.Message, true);
        }
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
