using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FhirMcp.Core.Fhir;

// For now this is a smoke-test command line, so the FHIR client can be tried
// against a live server before the MCP layer exists. The MCP host replaces it next.
//
//   dotnet run --project src/FhirMcp.Server -- search --name smith
//   dotnet run --project src/FhirMcp.Server -- search --birthdate 1970-01-01 --max 5
//
// The server defaults to the SMART Health IT R4 sandbox (synthetic patients only).
// Override it with the FHIR_BASE_URL environment variable.

const string DefaultBaseUrl = "https://r4.smarthealthit.org/";

if (args.Length == 0 || args[0] != "search")
{
    Console.Error.WriteLine("Usage: search [--name <text>] [--birthdate yyyy-MM-dd] [--max <1-50>]");
    return 2;
}

string? name = null;
DateOnly? birthDate = null;
var max = 10;

for (var i = 1; i < args.Length; i++)
{
    var value = i + 1 < args.Length ? args[i + 1] : null;
    switch (args[i])
    {
        case "--name" when value is not null:
            name = value; i++;
            break;
        case "--birthdate" when value is not null
            && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var bd):
            birthDate = bd; i++;
            break;
        case "--max" when value is not null && int.TryParse(value, out var m):
            max = m; i++;
            break;
        default:
            Console.Error.WriteLine($"Unrecognized or incomplete option: {args[i]}");
            return 2;
    }
}

var baseUrl = Environment.GetEnvironmentVariable("FHIR_BASE_URL") ?? DefaultBaseUrl;
if (!baseUrl.EndsWith('/')) baseUrl += "/";

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
var client = new FhirApiClient(http);

try
{
    var patients = await client.SearchPatientsAsync(new PatientSearchCriteria(name, birthDate, max));
    var json = JsonSerializer.Serialize(patients, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    });
    Console.WriteLine(json);
    Console.Error.WriteLine($"{patients.Count} patient(s) from {baseUrl}");
    return 0;
}
catch (Exception ex) when (ex is FhirRequestException or ArgumentException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
