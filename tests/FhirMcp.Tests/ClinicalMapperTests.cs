using System.Text.Json;
using FhirMcp.Core.Fhir;
using FhirMcp.Core.Mapping;
using Xunit;

namespace FhirMcp.Tests;

/// <summary>Turning clinical FHIR resources into compact, readable summaries.</summary>
public class ClinicalMapperTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static readonly IReadOnlyDictionary<string, JsonElement> NoIncludes = new Dictionary<string, JsonElement>();

    [Fact]
    public void Medication_named_inline_with_built_dosage()
    {
        var request = Json("""
            { "resourceType": "MedicationRequest", "status": "active", "authoredOn": "2004-07-14T10:00:00Z",
              "medicationCodeableConcept": { "coding": [ { "system": "http://www.nlm.nih.gov/research/umls/rxnorm", "code": "316672", "display": "Simvastatin 10 MG" } ] },
              "reasonCode": [ { "text": "Hyperlipidemia" } ],
              "dosageInstruction": [ { "timing": { "repeat": { "frequency": 1, "period": 1, "periodUnit": "d" } },
                                       "doseAndRate": [ { "doseQuantity": { "value": 1 } } ] } ] }
            """);

        var m = ClinicalMapper.MapMedication(request, NoIncludes);

        Assert.Equal("Simvastatin 10 MG", m.Name);
        Assert.Equal("316672", m.RxNormCode);
        Assert.Equal("active", m.Status);
        Assert.Equal(new DateOnly(2004, 7, 14), m.Ordered);
        Assert.Equal("dose 1, once a day", m.Dosage);
        Assert.Equal("Hyperlipidemia", m.Reason);
    }

    [Theory]
    [InlineData("""{ "text": "Take 1 tablet by mouth nightly" }""", "Take 1 tablet by mouth nightly")]
    [InlineData("""{ "asNeededBoolean": true }""", "as needed")]
    [InlineData("""{ "timing": { "repeat": { "frequency": 2, "period": 1, "periodUnit": "d" } }, "doseAndRate": [ { "doseQuantity": { "value": 500, "unit": "mg" } } ] }""", "500 mg, twice a day")]
    [InlineData("""{ "timing": { "repeat": { "frequency": 1, "period": 2, "periodUnit": "wk" } } }""", "once every 2 weeks")]
    [InlineData("""{ "asNeededCodeableConcept": { "text": "pain" } }""", "as needed for pain")]
    public void Medication_dosage_reads_like_a_prescription(string dosage, string expected)
    {
        var request = Json($$"""{ "medicationCodeableConcept": { "text": "X" }, "dosageInstruction": [ {{dosage}} ] }""");

        Assert.Equal(expected, ClinicalMapper.MapMedication(request, NoIncludes).Dosage);
    }

    [Fact]
    public void Medication_given_by_reference_is_resolved_from_included_resources()
    {
        var request = Json("""{ "medicationReference": { "reference": "Medication/med-1" } }""");
        var medication = Json("""
            { "resourceType": "Medication", "id": "med-1",
              "code": { "coding": [ { "system": "http://www.nlm.nih.gov/research/umls/rxnorm", "code": "4917", "display": "Nitroglycerin" } ] } }
            """);

        var m = ClinicalMapper.MapMedication(request, new Dictionary<string, JsonElement> { ["Medication/med-1"] = medication });

        Assert.Equal("Nitroglycerin", m.Name);
        Assert.Equal("4917", m.RxNormCode);
    }

    [Fact]
    public void Medication_reference_falls_back_to_its_display_then_to_a_placeholder()
    {
        var withDisplay = Json("""{ "medicationReference": { "reference": "Medication/missing", "display": "Lisinopril 10 MG" } }""");
        var bare = Json("""{ "medicationReference": { "reference": "Medication/missing" } }""");

        Assert.Equal("Lisinopril 10 MG", ClinicalMapper.MapMedication(withDisplay, NoIncludes).Name);
        Assert.Equal("(medication not identified)", ClinicalMapper.MapMedication(bare, NoIncludes).Name);
    }

    [Theory]
    [InlineData("Medication/123", "Medication/123")]
    [InlineData("https://example.org/fhir/Medication/123", "Medication/123")]
    [InlineData("Medication/123/_history/2", "Medication/123")]
    [InlineData("123", null)]
    [InlineData("", null)]
    public void References_normalize_to_type_and_id(string reference, string? expected)
    {
        Assert.Equal(expected, ClinicalMapper.NormalizeReference(reference));
    }

    [Fact]
    public void Condition_has_status_dates_and_snomed_code()
    {
        var condition = Json("""
            { "clinicalStatus": { "coding": [ { "code": "resolved" } ] },
              "verificationStatus": { "coding": [ { "code": "confirmed" } ] },
              "code": { "coding": [ { "system": "http://snomed.info/sct", "code": "44465007", "display": "Sprain of ankle" } ], "text": "Sprain of ankle" },
              "onsetDateTime": "2019-12-13T01:23:28+00:00", "abatementDateTime": "2020-01-17T01:23:28+00:00" }
            """);

        var c = ClinicalMapper.MapCondition(condition);

        Assert.Equal("Sprain of ankle", c.Name);
        Assert.Equal("resolved", c.ClinicalStatus);
        Assert.Equal("confirmed", c.VerificationStatus);
        Assert.Equal(new DateOnly(2019, 12, 13), c.Onset);
        Assert.Equal(new DateOnly(2020, 1, 17), c.Resolved);
        Assert.Equal("44465007", c.SnomedCode);
    }

    [Fact]
    public void Encounter_setting_reasons_and_discharge_are_readable()
    {
        var encounter = Json("""
            { "status": "finished", "class": { "code": "IMP" },
              "type": [ { "text": "Encounter Inpatient" } ],
              "period": { "start": "1953-11-29T01:23:28+00:00", "end": "1953-11-30T02:32:28+00:00" },
              "reasonCode": [ { "coding": [ { "display": "Appendicitis" } ] } ],
              "hospitalization": { "dischargeDisposition": { "text": "Discharged home" } } }
            """);

        var e = ClinicalMapper.MapEncounter(encounter);

        Assert.Equal("inpatient", e.Setting);
        Assert.Equal("Encounter Inpatient", e.Type);
        Assert.Equal(new[] { "Appendicitis" }, e.Reasons.ToArray());
        Assert.Equal(new DateOnly(1953, 11, 29), e.Start);
        Assert.Equal(new DateOnly(1953, 11, 30), e.End);
        Assert.Equal("Discharged home", e.DischargedTo);
    }

    [Fact]
    public void Observation_quantity_is_rounded_to_four_significant_digits()
    {
        var observation = Json("""
            { "code": { "coding": [ { "system": "http://loinc.org", "code": "2339-0" } ], "text": "Glucose" },
              "category": [ { "coding": [ { "code": "laboratory" } ] } ],
              "effectiveDateTime": "2014-12-17T01:23:28+00:00",
              "valueQuantity": { "value": 77.92600836244159, "unit": "mg/dL" },
              "interpretation": [ { "coding": [ { "display": "Normal" } ] } ],
              "referenceRange": [ { "low": { "value": 70, "unit": "mg/dL" }, "high": { "value": 99, "unit": "mg/dL" } } ] }
            """);

        var o = ClinicalMapper.MapObservation(observation);

        Assert.Equal("Glucose", o.Name);
        Assert.Equal("77.93 mg/dL", o.Value);
        Assert.Equal("Normal", o.Interpretation);
        Assert.Equal("70 mg/dL to 99 mg/dL", o.ReferenceRange);
        Assert.Equal("laboratory", o.Category);
        Assert.Equal("2339-0", o.LoincCode);
        Assert.Equal(new DateOnly(2014, 12, 17), o.Date);
    }

    [Fact]
    public void Observation_components_are_listed_by_name()
    {
        var observation = Json("""
            { "code": { "text": "Blood Pressure" },
              "component": [
                { "code": { "text": "Systolic Blood Pressure" }, "valueQuantity": { "value": 124.7, "unit": "mm[Hg]" } },
                { "code": { "text": "Diastolic Blood Pressure" }, "valueQuantity": { "value": 81.68, "unit": "mm[Hg]" } } ] }
            """);

        Assert.Equal(
            "Systolic Blood Pressure: 124.7 mm[Hg]; Diastolic Blood Pressure: 81.68 mm[Hg]",
            ClinicalMapper.MapObservation(observation).Value);
    }

    [Theory]
    [InlineData("""{ "valueCodeableConcept": { "text": "Never smoker" } }""", "Never smoker")]
    [InlineData("""{ "valueString": "Positive" }""", "Positive")]
    [InlineData("""{ "valueBoolean": false }""", "no")]
    [InlineData("""{ "valueInteger": 3 }""", "3")]
    [InlineData("""{ "valueQuantity": { "value": 5, "comparator": "<", "unit": "mg/L" } }""", "<5 mg/L")]
    [InlineData("""{ "dataAbsentReason": { "text": "Patient refused" } }""", "not available (Patient refused)")]
    public void Observation_values_of_every_kind_read_as_text(string valueJson, string expected)
    {
        var observation = Json($$"""{ "code": { "text": "Test" }, {{valueJson[1..^1]}} }""");

        Assert.Equal(expected, ClinicalMapper.MapObservation(observation).Value);
    }

    [Fact]
    public void Allergy_lists_each_reaction_once()
    {
        var allergy = Json("""
            { "clinicalStatus": { "coding": [ { "code": "active" } ] }, "criticality": "high",
              "code": { "text": "Peanut" }, "recordedDate": "2010-04-02",
              "reaction": [ { "manifestation": [ { "text": "Hives" }, { "text": "Wheezing" } ] },
                            { "manifestation": [ { "text": "hives" } ] } ] }
            """);

        var a = ClinicalMapper.MapAllergy(allergy);

        Assert.Equal("Peanut", a.Substance);
        Assert.Equal("high", a.Criticality);
        Assert.Equal(new[] { "Hives", "Wheezing" }, a.Reactions.ToArray());
        Assert.Equal(new DateOnly(2010, 4, 2), a.Recorded);
    }

    [Theory]
    [InlineData(77.92600836244159, "77.93")]
    [InlineData(0.012345, "0.01235")]
    [InlineData(124.7, "124.7")]
    [InlineData(5, "5")]
    [InlineData(123456.7, "123457")]
    public void Numbers_are_formatted_for_reading(double value, string expected)
    {
        Assert.Equal(expected, FhirJson.FormatNumber(value));
    }
}
