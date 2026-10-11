using System.Text.Json;
using System.Text.Json.Nodes;
using FhirMcp.Core.Models;
using FhirMcp.Core.Privacy;
using FhirMcp.Server;
using FhirMcp.Server.Tools;
using Xunit;

namespace FhirMcp.Tests;

public class PrivacyPolicyTests
{
    private static readonly string[] Tools = { "search_patients", "get_conditions", "get_medications" };

    private static PrivacyPolicy Read(params (string Key, string Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Key, v => v.Value);
        return PrivacyPolicy.FromEnvironment(k => map.GetValueOrDefault(k), "/home/test", Tools);
    }

    [Fact]
    public void Defaults_to_full_detail_auditing_to_the_home_folder_and_all_tools()
    {
        var policy = Read();

        Assert.Equal(PrivacyMode.Full, policy.Mode);
        Assert.Equal(Path.Combine("/home/test", ".fhir-mcp", "audit.jsonl"), policy.AuditLogPath);
        Assert.Null(policy.EnabledTools);
        Assert.True(policy.IsToolEnabled("get_medications"));
    }

    [Theory]
    [InlineData("deidentified")]
    [InlineData("De-Identified")]
    [InlineData(" DEIDENTIFIED ")]
    public void Reads_deidentified_mode(string value)
    {
        Assert.Equal(PrivacyMode.Deidentified, Read((PrivacyPolicy.PrivacyVariable, value)).Mode);
    }

    [Fact]
    public void Refuses_an_unknown_privacy_mode_instead_of_falling_back()
    {
        var ex = Assert.Throws<ArgumentException>(() => Read((PrivacyPolicy.PrivacyVariable, "anonymous")));

        Assert.Contains("anonymous", ex.Message);
    }

    [Fact]
    public void Audit_can_be_turned_off_or_pointed_elsewhere()
    {
        Assert.Null(Read((PrivacyPolicy.AuditLogVariable, "OFF")).AuditLogPath);
        Assert.Equal("/var/log/fhir.jsonl", Read((PrivacyPolicy.AuditLogVariable, "/var/log/fhir.jsonl")).AuditLogPath);
        Assert.Equal(Path.Combine("/home/test", "logs/a.jsonl"), Read((PrivacyPolicy.AuditLogVariable, "~/logs/a.jsonl")).AuditLogPath);
    }

    [Fact]
    public void Limits_tools_to_the_allowlist()
    {
        var policy = Read((PrivacyPolicy.ToolsVariable, " search_patients , get_conditions "));

        Assert.True(policy.IsToolEnabled("search_patients"));
        Assert.True(policy.IsToolEnabled("get_conditions"));
        Assert.False(policy.IsToolEnabled("get_medications"));
    }

    [Fact]
    public void Refuses_misspelled_tool_names_and_lists_the_real_ones()
    {
        var ex = Assert.Throws<ArgumentException>(() => Read((PrivacyPolicy.ToolsVariable, "search_patients,get_meds")));

        Assert.Contains("get_meds", ex.Message);
        Assert.Contains("get_medications", ex.Message);
    }

    [Fact]
    public void The_server_knows_every_tool_by_name()
    {
        Assert.Equal(
            new[] { "get_allergies", "get_conditions", "get_encounters", "get_medications", "get_observations", "get_patient_summary", "search_patients" },
            PolicyFilters.KnownTools.ToArray());
    }
}

public class DeidentifierTests
{
    private static JsonNode Deid(object value) =>
        JsonNode.Parse(Deidentifier.Apply(ToolSerializer.Serialize(value)))!;

    private static PatientSummary Patient(int? age = 52, string? birth = "1974-02-28") => new(
        "pat-1", "Reyna Ziemann", new[] { "Reyna Rice (maiden)" }, "female",
        birth is null ? null : DateOnly.Parse(birth), age, "Raynham", "Massachusetts", false);

    [Fact]
    public void Patient_names_and_city_are_removed_and_birth_date_reduced_to_the_year()
    {
        var p = Deid(new PatientSearchResponse(1, "name", null, new[] { Patient() }))["patients"]![0]!;

        Assert.Equal("pat-1", p["id"]!.GetValue<string>());
        Assert.Equal(Deidentifier.Redacted, p["name"]!.GetValue<string>());
        Assert.Empty(p["otherNames"]!.AsArray());
        Assert.Null(p["city"]);
        Assert.Equal("Massachusetts", p["state"]!.GetValue<string>());
        Assert.Equal("1974", p["birthDate"]!.GetValue<string>());
        Assert.Equal(52, p["age"]!.GetValue<int>());
    }

    [Fact]
    public void Ages_over_89_are_grouped_and_their_birth_year_removed()
    {
        var p = Deid(new PatientSearchResponse(1, "name", null, new[] { Patient(age: 93, birth: "1933-05-01") }))["patients"]![0]!;

        Assert.Equal("90 or older", p["age"]!.GetValue<string>());
        Assert.Null(p["birthDate"]);
    }

    [Fact]
    public void Clinical_dates_are_reduced_to_the_year_but_clinical_names_are_kept()
    {
        var list = new ListResponse<ConditionSummary>("pat-1", 1, false, null, new[]
        {
            new ConditionSummary("Hypertension", "active", "confirmed", new DateOnly(1992, 10, 8), new DateOnly(2001, 1, 2), "38341003"),
        });

        var c = Deid(list)["items"]![0]!;

        Assert.Equal("Hypertension", c["name"]!.GetValue<string>());
        Assert.Equal("1992", c["onset"]!.GetValue<string>());
        Assert.Equal("2001", c["resolved"]!.GetValue<string>());
    }

    [Fact]
    public void The_patient_inside_a_summary_is_redacted_too()
    {
        var overview = new PatientOverview(Patient(), [], [], [], [], new EncounterSummary(
            new DateOnly(2020, 5, 27), new DateOnly(2020, 5, 27), "ambulatory", "Asthma follow-up", [], "finished", null));

        var json = Deid(overview);

        Assert.Equal(Deidentifier.Redacted, json["patient"]!["name"]!.GetValue<string>());
        Assert.Equal("2020", json["lastEncounter"]!["start"]!.GetValue<string>());
    }

    [Fact]
    public void Responses_say_they_were_de_identified()
    {
        var json = Deid(new PatientSearchResponse(0, "name", null, []));

        Assert.Equal(Deidentifier.Notice, json["privacy"]!.GetValue<string>());
    }

    [Fact]
    public void Full_mode_leaves_responses_unchanged()
    {
        var response = new PatientSearchResponse(1, "name", null, new[] { Patient() });

        Assert.Equal(ToolSerializer.Serialize(response), new ToolOutput(PrivacyMode.Full).Serialize(response));
        Assert.NotEqual(ToolSerializer.Serialize(response), new ToolOutput(PrivacyMode.Deidentified).Serialize(response));
    }
}

public class AuditLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fhir-mcp-audit-" + Guid.NewGuid().ToString("N"));
    private string LogPath => Path.Combine(_dir, "nested", "audit.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static IReadOnlyDictionary<string, JsonElement> Args(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void Records_who_was_accessed_without_copying_names_or_birth_dates()
    {
        var log = new AuditLog(LogPath, new FixedTime(2026, 10, 11));

        var record = log.Write("search_patients", "ok", TimeSpan.FromMilliseconds(120),
            Args("""{ "name": "Reyna Ziemann", "birthDate": "1974-02-28", "maxResults": 5, "contains": null }"""),
            """{ "count": 2, "patients": [ { "id": "p-2" }, { "id": "p-1" } ] }""",
            PrivacyMode.Full, "Claude Desktop", null);

        Assert.Equal("[redacted]", record.Arguments["name"]);
        Assert.Equal("[redacted]", record.Arguments["birthDate"]);
        Assert.Equal("5", record.Arguments["maxResults"]);
        Assert.False(record.Arguments.ContainsKey("contains"));
        Assert.Equal(new[] { "p-1", "p-2" }, record.PatientIds.ToArray());
        Assert.Equal(2, record.ItemCount);
        Assert.Equal(AuditLog.Genesis, record.Previous);

        var line = File.ReadAllLines(LogPath).Single();
        Assert.DoesNotContain("Ziemann", line);
        Assert.DoesNotContain("1974", line);
    }

    [Fact]
    public void Picks_up_the_patient_from_arguments_and_summaries()
    {
        Assert.Equal(new[] { "p-9" }, AuditLog.PatientIdsIn(Args("""{ "patientId": "p-9" }"""), """{ "patientId": "p-9", "count": 0 }""").ToArray());
        Assert.Equal(new[] { "p-3" }, AuditLog.PatientIdsIn(null, """{ "patient": { "id": "p-3" } }""").ToArray());
        Assert.Empty(AuditLog.PatientIdsIn(null, "Unknown category \"labs\"."));
    }

    [Fact]
    public void Each_line_chains_to_the_one_before_and_the_chain_continues_after_a_restart()
    {
        var first = new AuditLog(LogPath);
        first.Write("get_conditions", "ok", TimeSpan.Zero, Args("""{ "patientId": "p-1" }"""), null, PrivacyMode.Full, null, null);
        first.Write("get_medications", "denied", TimeSpan.Zero, Args("""{ "patientId": "p-1" }"""), null, PrivacyMode.Full, null, "disabled");

        var reopened = new AuditLog(LogPath);
        var third = reopened.Write("get_allergies", "ok", TimeSpan.Zero, null, null, PrivacyMode.Deidentified, null, null);

        var lines = File.ReadAllLines(LogPath);
        Assert.Equal(3, lines.Length);
        Assert.Equal(AuditLog.Hash(lines[1]), third.Previous);
        Assert.Null(AuditLog.Verify(LogPath));
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("delete")]
    [InlineData("reorder")]
    public void Verification_catches_edited_deleted_and_reordered_lines(string tampering)
    {
        var log = new AuditLog(LogPath);
        for (var i = 0; i < 4; i++)
        {
            log.Write("get_conditions", "ok", TimeSpan.Zero, Args($$"""{ "patientId": "p-{{i}}" }"""), null, PrivacyMode.Full, null, null);
        }

        var lines = File.ReadAllLines(LogPath).ToList();
        switch (tampering)
        {
            case "edit": lines[1] = lines[1].Replace("p-1", "p-7"); break;
            case "delete": lines.RemoveAt(2); break;
            case "reorder": (lines[1], lines[2]) = (lines[2], lines[1]); break;
        }
        File.WriteAllLines(LogPath, lines);

        Assert.NotNull(AuditLog.Verify(LogPath));
    }
}
