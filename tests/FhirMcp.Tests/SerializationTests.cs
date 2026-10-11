using System.Text.Json;
using FhirMcp.Core.Models;
using FhirMcp.Server;
using FhirMcp.Server.Tools;

namespace FhirMcp.Tests;

/// <summary>
/// Verifies the serialization contract for all MCP tool response types.
/// Tools now return pre-serialized JSON strings using ToolSerializer.Options
/// (camelCase, nulls omitted), which sidesteps the MCP SDK schema generator
/// entirely. These tests pin the output format to catch regressions.
/// </summary>
public class SerializationTests
{
    private static string Serialize<T>(T value) => ToolSerializer.Serialize(value);
    private static bool Has(string json, string key) => json.Contains($"\"{key}\"");

    // ---- PatientSearchResponse ----

    [Fact]
    public void PatientSearchResponse_omits_null_note()
    {
        var r = new PatientSearchResponse(0, "birth date", null, []);
        Assert.False(Has(Serialize(r), "note"), "note should not appear when null");
    }

    [Fact]
    public void PatientSearchResponse_includes_note_when_set()
    {
        var r = new PatientSearchResponse(1, "name (start of words)", "Some note.", []);
        Assert.True(Has(Serialize(r), "note"), "note should appear when set");
    }

    [Fact]
    public void PatientSearchResponse_always_includes_required_fields()
    {
        var r = new PatientSearchResponse(0, "birth date", null, []);
        var json = Serialize(r);
        Assert.True(Has(json, "count"),     "count must always appear");
        Assert.True(Has(json, "matchedBy"), "matchedBy must always appear");
        Assert.True(Has(json, "patients"),  "patients must always appear");
    }

    [Fact]
    public void PatientSearchResponse_uses_camelCase()
    {
        var r = new PatientSearchResponse(1, "birth date", null,
            [new PatientSummary("p1", "Test Patient", [], "male", new DateOnly(1980, 1, 1), 45, "Dallas", "TX", false)]);
        var json = Serialize(r);
        Assert.True(Has(json, "matchedBy"),  "matchedBy (camelCase) expected");
        Assert.False(Has(json, "MatchedBy"), "MatchedBy (PascalCase) must not appear");
    }

    // ---- ListResponse<T> ----

    [Fact]
    public void ListResponse_omits_null_note()
    {
        var r = new ListResponse<MedicationSummary>("p1", 0, false, null, []);
        Assert.False(Has(Serialize(r), "note"), "note should not appear when null");
    }

    [Fact]
    public void ListResponse_includes_note_when_set()
    {
        var r = new ListResponse<MedicationSummary>("p1", 0, false, "No active orders.", []);
        Assert.True(Has(Serialize(r), "note"), "note should appear when set");
    }

    [Fact]
    public void ListResponse_always_includes_required_fields()
    {
        var r = new ListResponse<MedicationSummary>("p1", 0, false, null, []);
        var json = Serialize(r);
        Assert.True(Has(json, "patientId"),     "patientId must always appear");
        Assert.True(Has(json, "count"),         "count must always appear");
        Assert.True(Has(json, "moreAvailable"), "moreAvailable must always appear");
        Assert.True(Has(json, "items"),         "items must always appear");
    }

    // ---- PatientOverview ----

    [Fact]
    public void PatientOverview_omits_null_lastEncounter()
    {
        var patient = new PatientSummary("p1", "Test Patient", [], null, null, null, null, null, false);
        var overview = new PatientOverview(patient, [], [], [], [], null);
        Assert.False(Has(Serialize(overview), "lastEncounter"), "lastEncounter should not appear when null");
    }

    // ---- PatientSummary ----

    [Fact]
    public void PatientSummary_omits_null_fields()
    {
        var p = new PatientSummary("p1", "Test Patient", [], null, null, null, null, null, false);
        var json = Serialize(p);
        Assert.False(Has(json, "gender"),    "gender should not appear when null");
        Assert.False(Has(json, "birthDate"), "birthDate should not appear when null");
        Assert.False(Has(json, "age"),       "age should not appear when null");
        Assert.False(Has(json, "city"),      "city should not appear when null");
        Assert.False(Has(json, "state"),     "state should not appear when null");
    }

    [Fact]
    public void PatientSummary_always_includes_required_fields()
    {
        var p = new PatientSummary("p1", "Test Patient", [], null, null, null, null, null, false);
        var json = Serialize(p);
        Assert.True(Has(json, "id"),         "id must always appear");
        Assert.True(Has(json, "name"),       "name must always appear");
        Assert.True(Has(json, "otherNames"), "otherNames must always appear");
        Assert.True(Has(json, "deceased"),   "deceased must always appear");
    }

    // ---- MedicationSummary ----

    [Fact]
    public void MedicationSummary_omits_null_fields()
    {
        var m = new MedicationSummary("Aspirin", null, null, null, null, null);
        var json = Serialize(m);
        Assert.False(Has(json, "status"),     "status should not appear when null");
        Assert.False(Has(json, "ordered"),    "ordered should not appear when null");
        Assert.False(Has(json, "dosage"),     "dosage should not appear when null");
        Assert.False(Has(json, "reason"),     "reason should not appear when null");
        Assert.False(Has(json, "rxNormCode"), "rxNormCode should not appear when null");
    }

    // ---- ConditionSummary ----

    [Fact]
    public void ConditionSummary_omits_null_fields()
    {
        var c = new ConditionSummary("Hypertension", null, null, null, null, null);
        var json = Serialize(c);
        Assert.False(Has(json, "clinicalStatus"),     "clinicalStatus should not appear when null");
        Assert.False(Has(json, "verificationStatus"), "verificationStatus should not appear when null");
        Assert.False(Has(json, "onset"),              "onset should not appear when null");
        Assert.False(Has(json, "resolved"),           "resolved should not appear when null");
        Assert.False(Has(json, "snomedCode"),         "snomedCode should not appear when null");
    }

    // ---- EncounterSummary ----

    [Fact]
    public void EncounterSummary_omits_null_fields()
    {
        var e = new EncounterSummary(null, null, null, null, [], null, null);
        var json = Serialize(e);
        Assert.False(Has(json, "start"),        "start should not appear when null");
        Assert.False(Has(json, "end"),          "end should not appear when null");
        Assert.False(Has(json, "setting"),      "setting should not appear when null");
        Assert.False(Has(json, "type"),         "type should not appear when null");
        Assert.False(Has(json, "status"),       "status should not appear when null");
        Assert.False(Has(json, "dischargedTo"), "dischargedTo should not appear when null");
    }

    // ---- ObservationSummary ----

    [Fact]
    public void ObservationSummary_omits_null_fields()
    {
        var o = new ObservationSummary("Blood Glucose", null, null, null, null, null, null);
        var json = Serialize(o);
        Assert.False(Has(json, "date"),           "date should not appear when null");
        Assert.False(Has(json, "value"),          "value should not appear when null");
        Assert.False(Has(json, "interpretation"), "interpretation should not appear when null");
        Assert.False(Has(json, "referenceRange"), "referenceRange should not appear when null");
        Assert.False(Has(json, "category"),       "category should not appear when null");
        Assert.False(Has(json, "loincCode"),      "loincCode should not appear when null");
    }

    // ---- AllergySummary ----

    [Fact]
    public void AllergySummary_omits_null_fields()
    {
        var a = new AllergySummary("Penicillin", null, null, [], null);
        var json = Serialize(a);
        Assert.False(Has(json, "clinicalStatus"), "clinicalStatus should not appear when null");
        Assert.False(Has(json, "criticality"),    "criticality should not appear when null");
        Assert.False(Has(json, "recorded"),       "recorded should not appear when null");
    }
}
