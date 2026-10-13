using System.Text.Json;
using System.Text.Json.Nodes;
using FhirMcp.Core.Fhir;
using FhirMcp.Server;
using FhirMcp.Server.Tools;

namespace FhirMcp.Eval;

/// <summary>
/// Freezes what the server's tools return for each evaluation patient, so the "answer" mode is
/// reproducible: every model reads the same record, even if the sandbox changes later.
/// Each patient's file maps a tool call (as text) to the exact JSON the tool returned.
/// </summary>
internal static class Snapshot
{
    /// <summary>The tool calls that make up one patient's chart for the answer mode.</summary>
    public static IReadOnlyList<(string Key, Func<PatientTools, ClinicalTools, string, Task<string>> Call)> Calls { get; } =
    [
        ("get_patient_summary", (_, c, id) => c.GetPatientSummary(id)),
        ("get_medications status=all", (_, c, id) => c.GetMedications(id, "all", 50)),
        ("get_conditions status=all", (_, c, id) => c.GetConditions(id, "all", 50)),
        ("get_allergies", (_, c, id) => c.GetAllergies(id)),
        ("get_observations category=laboratory latestOnly=true", (_, c, id) => c.GetObservations(id, "laboratory", null, null, null, true, 100)),
        ("get_observations category=vital-signs latestOnly=true", (_, c, id) => c.GetObservations(id, "vital-signs", null, null, null, true, 50)),
        ("get_observations text=a1c", (_, c, id) => c.GetObservations(id, null, "a1c", null, null, false, 50)),
        ("get_encounters", (_, c, id) => c.GetEncounters(id, null, 50)),
    ];

    public static async Task<int> RunAsync(string baseUrl, IReadOnlyDictionary<string, string> patients, string folder)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };
        var fhir = new FhirApiClient(http);
        var output = new ToolOutput(Core.Privacy.PrivacyMode.Full);
        var patientTools = new PatientTools(fhir, output);
        var clinicalTools = new ClinicalTools(fhir, output);
        Directory.CreateDirectory(folder);

        foreach (var (key, id) in patients)
        {
            var chart = new JsonObject
            {
                ["patientId"] = id,
                ["source"] = baseUrl,
                ["capturedOn"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            };
            var tools = new JsonObject();
            foreach (var (callKey, call) in Calls)
            {
                tools[callKey] = JsonNode.Parse(await call(patientTools, clinicalTools, id));
            }
            chart["tools"] = tools;

            var path = Path.Combine(folder, key + ".json");
            await File.WriteAllTextAsync(path, chart.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }) + "\n");
            Console.WriteLine($"{key}: {new FileInfo(path).Length / 1024.0:0.0} KB");
        }
        return 0;
    }
}
