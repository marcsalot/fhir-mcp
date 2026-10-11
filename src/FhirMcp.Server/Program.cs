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
//   servers                           list the named test servers
//
// FHIR_MCP_SERVER picks a test server by name (smart, hapi or smart-login), or
// FHIR_BASE_URL sets any other server. The default is the SMART Health IT R4 sandbox,
// which holds synthetic patients only. FHIR_MCP_PRIVACY, FHIR_MCP_AUDIT_LOG and
// FHIR_MCP_TOOLS set the privacy policy (see PrivacyPolicy); FHIR_MCP_AUTH and the
// FHIR_MCP_SMART_* variables set sign-in (see SmartSettings and the README).

var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

FhirServerSettings server;
PrivacyPolicy policy;
SmartSettings smart;
try
{
    server = FhirServerSettings.FromEnvironment(Environment.GetEnvironmentVariable);
    policy = PrivacyPolicy.FromEnvironment(Environment.GetEnvironmentVariable, home, PolicyFilters.KnownTools);

    // A preset that expects sign-in (smart-login) turns it on and suggests its scope,
    // unless FHIR_MCP_AUTH or FHIR_MCP_SMART_SCOPE say otherwise.
    smart = SmartSettings.FromEnvironment(Environment.GetEnvironmentVariable, home, OperatingSystem.IsMacOS(),
        defaultEnabled: server.Preset?.SignIn ?? false, defaultScope: server.Preset?.DefaultScope);
}
catch (ArgumentException ex)
{
    // Refuse to start rather than run with a looser policy than the one intended.
    Console.Error.WriteLine($"fhir-mcp: {ex.Message}");
    return 2;
}

var baseUrl = server.BaseUrl;

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
    case "servers":
        return ListServers(server);
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
log.LogInformation("Starting fhir-mcp against {Server} with {Policy}; {Auth}", server.Describe(), policy.Describe(), smart.Describe());
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

static int ListServers(FhirServerSettings current)
{
    Console.WriteLine($"Pick one with {FhirServerSettings.ServerVariable}=<name>, or set {FhirServerSettings.BaseUrlVariable} for any other server.");
    Console.WriteLine();
    foreach (var preset in FhirServerSettings.Presets)
    {
        var marker = current.Preset?.Name == preset.Name ? "*" : " ";
        Console.WriteLine($"{marker} {preset.Name,-12} {preset.Description}");
        Console.WriteLine($"  {"",-12} {preset.BaseUrl}");
    }
    if (current.Preset is null)
    {
        Console.WriteLine();
        Console.WriteLine($"* Current: {current.BaseUrl} (from {FhirServerSettings.BaseUrlVariable})");
    }
    return 0;
}
