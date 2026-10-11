using System.Globalization;
using System.Text.Json;
using FhirMcp.Core.Auth;
using FhirMcp.Core.Fhir;
using FhirMcp.Core.Privacy;
using FhirMcp.Server;
using FhirMcp.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Runs as an MCP server over stdio by default. Other commands, run in a terminal:
//
//   search --name "reyna ziemann"     try a patient search
//   audit-verify [path]               check the audit log's hash chain
//   login [--scope "..."]             sign in with SMART on FHIR (opens the browser)
//   logout                            remove the saved sign-in
//   auth-status                       show who is signed in, without printing tokens
//
// FHIR_BASE_URL picks the server; it defaults to the SMART Health IT R4 sandbox,
// which holds synthetic patients only. FHIR_MCP_PRIVACY, FHIR_MCP_AUDIT_LOG and
// FHIR_MCP_TOOLS set the privacy policy (see PrivacyPolicy); FHIR_MCP_AUTH and the
// FHIR_MCP_SMART_* variables set sign-in (see SmartSettings and the README).

const string DefaultBaseUrl = "https://r4.smarthealthit.org/";

var baseUrl = Environment.GetEnvironmentVariable("FHIR_BASE_URL") ?? DefaultBaseUrl;
if (!baseUrl.EndsWith('/')) baseUrl += "/";

var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

PrivacyPolicy policy;
SmartSettings smart;
try
{
    policy = PrivacyPolicy.FromEnvironment(Environment.GetEnvironmentVariable, home, PolicyFilters.KnownTools);
    smart = SmartSettings.FromEnvironment(Environment.GetEnvironmentVariable, home, OperatingSystem.IsMacOS());
}
catch (ArgumentException ex)
{
    // Refuse to start rather than run with a looser policy than the one intended.
    Console.Error.WriteLine($"fhir-mcp: {ex.Message}");
    return 2;
}

// With SMART sign-in on, every FHIR request carries the signed-in user's access token.
var session = smart.Enabled
    ? new SmartSession(
        smart.CreateStore(),
        new SmartAuthClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }),
        baseUrl,
        AuthCommands.SignInHint(baseUrl))
    : null;

switch (args.FirstOrDefault())
{
    case "search":
        return await RunSearchCommandAsync(args, baseUrl, policy, session);
    case "audit-verify":
        return RunAuditVerify(args.Length > 1 ? args[1] : policy.AuditLogPath ?? PrivacyPolicy.DefaultAuditPath(home));
    case "login":
        return await AuthCommands.LoginAsync(args, baseUrl, smart);
    case "logout":
        return AuthCommands.Logout(baseUrl, smart);
    case "auth-status":
        return AuthCommands.Status(baseUrl, smart);
}

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol, so every log line must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

var fhirHttp = builder.Services.AddHttpClient<FhirApiClient>(http =>
{
    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(30);
});

if (session is not null)
{
    fhirHttp.AddHttpMessageHandler(() => new BearerTokenHandler(session));
    builder.Services.AddSingleton<IAccessScope>(session);
}

var audit = policy.AuditLogPath is null ? null : new AuditLog(policy.AuditLogPath);
builder.Services.AddSingleton(policy);
builder.Services.AddSingleton(new ToolOutput(policy));

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "fhir-mcp", Version = "0.3.0" })
    .WithStdioServerTransport()
    .WithTools<PatientTools>()
    .WithTools<ClinicalTools>()
    .WithPolicy(policy, audit, session is null ? null : () => session.User);

var app = builder.Build();
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("fhir-mcp");
log.LogInformation("Starting fhir-mcp against {BaseUrl} with {Policy}; {Auth}", baseUrl, policy.Describe(), smart.Describe());
if (session is not null)
{
    var tokens = session.Current;
    if (tokens is null)
    {
        log.LogWarning("Not signed in to {BaseUrl}. Tools will ask for a sign-in. {Hint}", baseUrl, AuthCommands.SignInHint(baseUrl));
    }
    else
    {
        log.LogInformation("Signed in as {User}; {Patient}", tokens.FhirUser ?? "(user not reported)",
            tokens.IsLimitedToPatient ? $"limited to patient {tokens.Patient}" : "not limited to one patient");
    }
}

await app.RunAsync();
return 0;

static async Task<int> RunSearchCommandAsync(string[] args, string baseUrl, PrivacyPolicy policy, SmartSession? session)
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

    using var http = session is null
        ? new HttpClient()
        : new HttpClient(new BearerTokenHandler(session, new HttpClientHandler()));
    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(30);
    var tools = new PatientTools(new FhirApiClient(http, access: session), new ToolOutput(policy));

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
