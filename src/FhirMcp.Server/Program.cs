using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FhirMcp.Core.Fhir;
using FhirMcp.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Runs as an MCP server over stdio by default. "search" runs a one-off command-line
// search instead, which is handy for checking the FHIR connection:
//
//   dotnet run --project src/FhirMcp.Server -- search --name "adolph smith"
//
// FHIR_BASE_URL picks the server; it defaults to the SMART Health IT R4 sandbox,
// which holds synthetic patients only.

const string DefaultBaseUrl = "https://r4.smarthealthit.org/";

var baseUrl = Environment.GetEnvironmentVariable("FHIR_BASE_URL") ?? DefaultBaseUrl;
if (!baseUrl.EndsWith('/')) baseUrl += "/";

if (args.Length > 0 && args[0] == "search")
{
    return await RunSearchCommandAsync(args, baseUrl);
}

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol, so every log line must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddHttpClient<FhirApiClient>(http =>
{
    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "fhir-mcp", Version = "0.1.0" })
    .WithStdioServerTransport()
    .WithTools<PatientTools>();

await builder.Build().RunAsync();
return 0;

static async Task<int> RunSearchCommandAsync(string[] args, string baseUrl)
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
    var tools = new PatientTools(new FhirApiClient(http));

    try
    {
        var response = await tools.SearchPatients(name, birthDate, contains, max);
        Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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
