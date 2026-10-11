using System.Net;
using FhirMcp.Core.Fhir;
using Xunit;

namespace FhirMcp.Tests;

/// <summary>What the client asks the server for, and how it shapes clinical results.</summary>
public class ClinicalClientTests
{
    private const string PatientId = "pat-001";

    private static (FhirApiClient Client, FakeHandler Handler) Create(Func<Uri, (HttpStatusCode, string)> respond) =>
        PatientSearchTests.Create(new FakeHandler(respond));

    private static string Bundle(string entries, string links = "") => $$"""
        { "resourceType": "Bundle", "type": "searchset", "link": [ {{links}} ], "entry": [ {{entries}} ] }
        """;

    private static string Entry(string resource, string mode = "match") =>
        $$"""{ "resource": {{resource}}, "search": { "mode": "{{mode}}" } }""";

    private static string Obs(string name, string loinc, string date, double value, string category = "laboratory") => Entry($$"""
        { "resourceType": "Observation", "code": { "coding": [ { "system": "http://loinc.org", "code": "{{loinc}}" } ], "text": "{{name}}" },
          "category": [ { "coding": [ { "code": "{{category}}" } ] } ],
          "effectiveDateTime": "{{date}}", "valueQuantity": { "value": {{value}}, "unit": "%" } }
        """);

    [Fact]
    public async Task Medications_ask_for_active_orders_newest_first_with_referenced_drugs_included()
    {
        var (client, handler) = Create(_ => (HttpStatusCode.OK, Bundle(string.Join(',',
            Entry("""{ "resourceType": "MedicationRequest", "status": "active", "medicationReference": { "reference": "Medication/m1" } }"""),
            Entry("""{ "resourceType": "Medication", "id": "m1", "code": { "text": "Metformin 500 MG" } }""", mode: "include")))));

        var result = await client.GetMedicationsAsync(PatientId, activeOnly: true, maxResults: 5);

        Assert.Equal(
            "https://example.org/fhir/MedicationRequest?patient=pat-001&status=active&_sort=-authoredon&_include=MedicationRequest:medication&_count=5",
            handler.Requests[0].AbsoluteUri);
        var med = Assert.Single(result.Items);
        Assert.Equal("Metformin 500 MG", med.Name);
    }

    [Theory]
    [InlineData("../Patient?_count=1000")]
    [InlineData("pat 001")]
    [InlineData("https://evil.example/Patient/1")]
    [InlineData("")]
    public async Task Malformed_patient_ids_are_rejected_before_any_request(string patientId)
    {
        var (client, handler) = Create(_ => (HttpStatusCode.OK, Bundle("")));

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetConditionsAsync(patientId));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetObservationsAsync(new ObservationQuery(patientId)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Active_conditions_include_recurrences_and_relapses()
    {
        var (client, handler) = Create(_ => (HttpStatusCode.OK, Bundle("")));

        await client.GetConditionsAsync(PatientId, activeOnly: true);

        Assert.Contains("clinical-status=active,recurrence,relapse", handler.Requests[0].AbsoluteUri);
    }

    [Fact]
    public async Task Latest_only_keeps_the_newest_result_of_each_test()
    {
        var (client, handler) = Create(_ => (HttpStatusCode.OK, Bundle(string.Join(',',
            Obs("Hemoglobin A1c", "4548-4", "2019-01-09", 6.0),
            Obs("Glucose", "2339-0", "2021-01-20", 88.0),
            Obs("Hemoglobin A1c", "4548-4", "2021-01-20", 6.2),
            Obs("Glucose", "2339-0", "2020-01-15", 90.0)))));

        var result = await client.GetObservationsAsync(new ObservationQuery(PatientId, Category: "laboratory", LatestOnly: true));

        Assert.Equal(new[] { "Glucose", "Hemoglobin A1c" }, result.Items.Select(o => o.Name).ToArray());
        Assert.All(result.Items, o => Assert.Equal(new DateOnly(2021, 1, 20), o.Date));
        Assert.Contains("_count=200", handler.Requests[0].AbsoluteUri);
        Assert.Contains("category=laboratory", handler.Requests[0].AbsoluteUri);
    }

    [Fact]
    public async Task Unknown_categories_are_rejected_with_the_valid_list()
    {
        var (client, _) = Create(_ => (HttpStatusCode.OK, Bundle("")));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => client.GetObservationsAsync(new ObservationQuery(PatientId, Category: "labs")));

        Assert.Contains("laboratory", ex.Message);
    }

    [Fact]
    public async Task A_test_name_the_server_cannot_find_is_matched_by_words_on_our_side()
    {
        var bloodPressure = Entry("""
            { "resourceType": "Observation", "code": { "text": "Blood Pressure" }, "effectiveDateTime": "2021-01-20",
              "component": [ { "code": { "text": "Systolic Blood Pressure" }, "valueQuantity": { "value": 124.7, "unit": "mm[Hg]" } } ] }
            """);
        var (client, handler) = Create(uri => uri.AbsoluteUri.Contains("code:text=")
            ? (HttpStatusCode.OK, Bundle(""))
            : (HttpStatusCode.OK, Bundle(string.Join(',',
                Obs("Hemoglobin A1c/Hemoglobin.total in Blood", "4548-4", "2021-01-20", 6.2),
                Obs("Glucose", "2339-0", "2021-01-20", 88.0),
                bloodPressure))));

        var a1c = await client.GetObservationsAsync(new ObservationQuery(PatientId, Text: "a1c"));
        var systolic = await client.GetObservationsAsync(new ObservationQuery(PatientId, Text: "systolic"));

        Assert.Equal("Hemoglobin A1c/Hemoglobin.total in Blood", Assert.Single(a1c.Items).Name);
        Assert.Contains("No test name started with \"a1c\"", a1c.Note);
        Assert.Equal("Blood Pressure", Assert.Single(systolic.Items).Name);
        Assert.Contains("code:text=a1c", handler.Requests[0].AbsoluteUri);
        Assert.DoesNotContain("code:text", handler.Requests[1].AbsoluteUri);
    }

    [Fact]
    public async Task The_name_scan_follows_next_pages_only_on_the_same_server()
    {
        var page1 = Bundle(
            Obs("Glucose", "2339-0", "2021-01-20", 88.0),
            links: """{ "relation": "next", "url": "https://example.org/fhir/Observation?page=2" }""");
        var page2 = Bundle(
            Obs("Hemoglobin A1c", "4548-4", "2015-01-20", 5.9),
            links: """{ "relation": "next", "url": "https://evil.example/fhir/Observation?page=3" }""");

        var (client, handler) = Create(uri =>
            uri.AbsoluteUri.Contains("code:text=") ? (HttpStatusCode.OK, Bundle(""))
            : uri.AbsoluteUri.Contains("page=2") ? (HttpStatusCode.OK, page2)
            : (HttpStatusCode.OK, page1));

        var result = await client.GetObservationsAsync(new ObservationQuery(PatientId, Text: "a1c"));

        Assert.Equal("Hemoglobin A1c", Assert.Single(result.Items).Name);
        Assert.DoesNotContain(handler.Requests, r => r.Host == "evil.example");
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Encounters_since_a_date_come_newest_first()
    {
        var (client, handler) = Create(_ => (HttpStatusCode.OK, Bundle(string.Join(',',
            Entry("""{ "resourceType": "Encounter", "class": { "code": "AMB" }, "period": { "start": "2019-11-24" } }"""),
            Entry("""{ "resourceType": "Encounter", "class": { "code": "EMER" }, "period": { "start": "2021-01-20" } }""")))));

        var result = await client.GetEncountersAsync(PatientId, since: new DateOnly(2019, 6, 1));

        Assert.Equal(new[] { "emergency", "ambulatory" }, result.Items.Select(e => e.Setting).ToArray());
        Assert.Contains("date=ge2019-06-01", handler.Requests[0].AbsoluteUri);
    }

    [Fact]
    public async Task The_overview_combines_six_searches()
    {
        var (client, handler) = Create(uri => uri.AbsolutePath switch
        {
            "/fhir/Patient/pat-001" => (HttpStatusCode.OK, """
                { "resourceType": "Patient", "id": "pat-001", "name": [ { "given": ["Ada"], "family": "Byrne" } ], "birthDate": "1980-06-15" }
                """),
            "/fhir/Condition" => (HttpStatusCode.OK, Bundle(Entry("""
                { "resourceType": "Condition", "clinicalStatus": { "coding": [ { "code": "active" } ] }, "code": { "text": "Prediabetes" } }
                """))),
            "/fhir/MedicationRequest" => (HttpStatusCode.OK, Bundle(Entry("""
                { "resourceType": "MedicationRequest", "status": "active", "medicationCodeableConcept": { "text": "Metformin 500 MG" } }
                """))),
            "/fhir/AllergyIntolerance" => (HttpStatusCode.OK, Bundle(Entry("""
                { "resourceType": "AllergyIntolerance", "code": { "text": "Peanut" } }
                """))),
            "/fhir/Observation" => (HttpStatusCode.OK, Bundle(Obs("Body Weight", "29463-7", "2021-01-20", 91.57, "vital-signs"))),
            "/fhir/Encounter" => (HttpStatusCode.OK, Bundle(Entry("""
                { "resourceType": "Encounter", "class": { "code": "AMB" }, "period": { "start": "2021-01-20" } }
                """))),
            _ => (HttpStatusCode.NotFound, "{}"),
        });

        var overview = await client.GetPatientOverviewAsync(PatientId);

        Assert.Equal("Ada Byrne", overview.Patient.Name);
        Assert.Equal("Prediabetes", Assert.Single(overview.ActiveConditions).Name);
        Assert.Equal("Metformin 500 MG", Assert.Single(overview.ActiveMedications).Name);
        Assert.Equal("Peanut", Assert.Single(overview.Allergies).Substance);
        Assert.Equal("Body Weight", Assert.Single(overview.LatestVitals).Name);
        Assert.Equal("ambulatory", overview.LastEncounter!.Setting);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task An_unknown_patient_gets_a_plain_message()
    {
        var (client, _) = Create(_ => (HttpStatusCode.NotFound, """
            { "resourceType": "OperationOutcome", "issue": [ { "severity": "error", "diagnostics": "Resource Patient/nope is not known" } ] }
            """));

        var ex = await Assert.ThrowsAsync<FhirRequestException>(() => client.GetPatientAsync("nope"));

        Assert.Equal("No patient with id \"nope\". Use an id returned by a patient search.", ex.Message);
    }
}
