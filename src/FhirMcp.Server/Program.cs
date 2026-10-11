using System.Globalization;
using System.Text.Json;
using FhirMcp.Core.Fhir;
using FhirMcp.Core.Privacy;
using FhirMcp.Server;
using FhirMcp.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Runs as an MCP server over stdio by default. Two commands are handy for checks:
//
//   dotnet run --project src/FhirMcp.Server -- search --name "reyna ziemann"
//   dotnet run --project src/FhirMcp.Server -- audit-verify [path]
//
// FHIR_BASE_URL picks the server; it defaults to the SMART Health IT R4 sandbox,
// which holds synthetic patients only. FHIR_MCP_PRIVACY, FHIR_MCP_AUDIT_LOG and
// FHIR_MCP_TOOLS set the privacy policy (see PrivacyPolicy and the README).

const string DefaultBaseUrl = "https://r4.smarthealthit.org/";

var baseUrl = Environment.GetEnvironmentVariable("FHIR_BASE_URL") ?? DefaultBaseUrl;
if (!baseUrl.EndsWith('/')) baseUrl += "/";

var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

PrivacyPolicy policy;
try
{
    policy = PrivacyPolicy.FromEnvironment(Environment.GetEnvironmentVariable, home, PolicyFilters.KnownTools);
}
catch (ArgumentException ex)
{
    // Refuse to start rather than run with a looser policy than the one intended.
    Console.Error.WriteLine($"fhir-mcp: {ex.Message}");
    return 2;
}

if (args.Length > 0 && args[0] == "search")
{
    return await RunSearchCommandAsync(args, baseUrl, policy);
}

if (args.Length > 0 && args[0] == "audit-verify")
{
    return RunAuditVerify(args.Length > 1 ? args[1] : policy.AuditLogPath ?? PrivacyPolicy.DefaultAuditPath(home));
}

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol, so every log line must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddHttpClient<FhirApiClient>(http =>
{
    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(30);
});

var audit = policy.AuditLogPath is null ? null : new AuditLog(policy.AuditLogPath);
builder.Services.AddSingleton(policy);
builder.Services.AddSingleton(new ToolOutput(policy));

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "fhir-mcp", Version = "0.2.0" })
    .WithStdioServerTransport()
    .WithTools<PatientTools>()
    .WithTools<ClinicalTools>()
    .WithPolicy(policy, audit);

var app = builder.Build();
app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("fhir-mcp")
    .LogInformation("Starting fhir-mcp against {BaseUrl} with {Policy}", baseUrl, policy.Describe());

await app.RunAsync();
return 0;

static async Task<int> RunSearchCommandAsync(string[] args, string baseUrl, PrivacyPolicy policy)
{
    string? name = null;
    string? birthDate = null;
    var max = 10;
    var contains = false;

    for (var i = 1; i < args.Length; i++)
    {
        var value = i + 1 < args.Length ? args[i + 1] : null;
        switch (args[i])
        {
            case "--name" when value is not null: name = value; i++; break;
            case "--birthdate" when value is not null: birthDate = value; i++; break;
            case "--max" when value is not null && int.TryParse(value, CultureInfo.InvariantCulture, out var m): max = m; i++; break;
            case "--contains": contains = true; break;
            default:
                Console.Error.WriteLine($"Unrecognized or incomplete option: {args[i]}");
                Console.Error.WriteLine("Usage: search [--name <text>] [--birthdate yyyy-MM-dd] [--max <1-50>] [--contains]");
                return 2;
        }
    }

    using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
    var tools = new PatientTools(new FhirApiClient(http), new ToolOutput(policy));

    try
    {
        // The tool returns compact JSON text (see ToolOutput); indent it for reading.
        var response = await tools.SearchPatients(name, birthDate, contains, max);
        using var doc = JsonDocument.Parse(response);
        Console.WriteLine(JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        return 0;
    }
    catch (ModelContextProtocol.McpException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int RunAuditVerify(string path)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"No audit log at {path}.");
        return 1;
    }

    var problem = AuditLog.Verify(path);
    var lines = File.ReadLines(path).Count(l => l.Length > 0);
    if (problem is null)
    {
        Console.WriteLine($"Audit log intact: {lines} records, hash chain verified ({path}).");
        return 0;
    }

    Console.Error.WriteLine($"Audit log FAILED verification: {problem} ({path})");
    return 1;
}
