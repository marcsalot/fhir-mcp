using System.Net;
using FhirMcp.Core.Fhir;
using Xunit;

namespace FhirMcp.Tests;

public class PatientSearchTests
{
    private static (FhirApiClient Client, FakeHandler Handler) Create(
        string body,
        HttpStatusCode status = HttpStatusCode.OK,
        TimeProvider? time = null)
    {
        var handler = new FakeHandler(body, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.org/fhir/") };
        return (new FhirApiClient(http, time ?? new FixedTime(2026, 6, 14)), handler);
    }

    private static readonly string Bundle = Fixtures.Read("patient-search-bundle.json");

    [Fact]
    public async Task Returns_only_patients_from_the_bundle()
    {
        var (client, _) = Create(Bundle);

        var result = await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "lopez"));

        Assert.Equal(new[] { "pat-001", "pat-002", "pat-003" }, result.Select(p => p.Id).ToArray());
    }

    [Fact]
    public async Task Prefers_the_official_name()
    {
        var (client, _) = Create(Bundle);

        var lopez = (await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "lopez")))[0];

        Assert.Equal("Maria Elena Lopez", lopez.Name);
    }

    [Fact]
    public async Task Uses_the_name_text_when_present()
    {
        var (client, _) = Create(Bundle);

        var smith = (await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "smith")))[1];

        Assert.Equal("John Q. Smith", smith.Name);
    }

    [Fact]
    public async Task Computes_age_the_day_before_and_on_the_birthday()
    {
        var (before, _) = Create(Bundle, time: new FixedTime(2026, 6, 14));
        var (onDay, _) = Create(Bundle, time: new FixedTime(2026, 6, 15));

        var ageBefore = (await before.SearchPatientsAsync(new PatientSearchCriteria(Name: "lopez")))[0].Age;
        var ageOnDay = (await onDay.SearchPatientsAsync(new PatientSearchCriteria(Name: "lopez")))[0].Age;

        Assert.Equal(45, ageBefore);
        Assert.Equal(46, ageOnDay);
    }

    [Fact]
    public async Task Partial_birth_dates_give_no_birth_date_or_age()
    {
        var (client, _) = Create(Bundle);

        var smith = (await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "smith")))[1];

        Assert.Null(smith.BirthDate);
        Assert.Null(smith.Age);
    }

    [Fact]
    public async Task Deceased_patients_are_flagged_and_have_no_age()
    {
        var (client, _) = Create(Bundle);

        var okafor = (await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "okafor")))[2];

        Assert.True(okafor.Deceased);
        Assert.Null(okafor.Age);
        Assert.Equal(new DateOnly(1940, 2, 29), okafor.BirthDate);
    }

    [Fact]
    public async Task Prefers_the_home_address_then_the_first()
    {
        var (client, _) = Create(Bundle);

        var result = await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "x"));

        Assert.Equal("Dallas", result[0].City);
        Assert.Null(result[1].City);
        Assert.Equal("Austin", result[2].City);
    }

    [Fact]
    public async Task Sends_an_escaped_query_and_asks_for_fhir_json()
    {
        var (client, handler) = Create(Bundle);

        await client.SearchPatientsAsync(new PatientSearchCriteria("maria lopez", new DateOnly(1980, 6, 15), 5));

        Assert.Equal(
            "https://example.org/fhir/Patient?name=maria%20lopez&birthdate=1980-06-15&_count=5",
            handler.LastRequest!.RequestUri!.AbsoluteUri);
        Assert.Contains(handler.LastRequest.Headers.Accept, h => h.MediaType == "application/fhir+json");
    }

    [Fact]
    public async Task Caps_results_even_if_the_server_returns_more()
    {
        var (client, _) = Create(Bundle);

        var result = await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "x", MaxResults: 2));

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task An_empty_search_returns_an_empty_list()
    {
        var (client, _) = Create("""{ "resourceType": "Bundle", "type": "searchset", "total": 0 }""");

        var result = await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "nobody"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Requires_a_name_or_birth_date()
    {
        var (client, handler) = Create(Bundle);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchPatientsAsync(new PatientSearchCriteria()));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Rejects_a_result_limit_over_50()
    {
        var (client, _) = Create(Bundle);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.SearchPatientsAsync(new PatientSearchCriteria(Name: "x", MaxResults: 51)));
    }

    [Fact]
    public async Task Server_errors_include_the_operation_outcome_text()
    {
        const string outcome = """
            { "resourceType": "OperationOutcome",
              "issue": [ { "severity": "error", "code": "invalid", "diagnostics": "Unknown search parameter 'nme'" } ] }
            """;
        var (client, _) = Create(outcome, HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<FhirRequestException>(
            () => client.SearchPatientsAsync(new PatientSearchCriteria(Name: "x")));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("Unknown search parameter 'nme'", ex.Message);
    }

    [Fact]
    public async Task A_response_that_is_not_a_bundle_is_rejected()
    {
        var (client, _) = Create("""{ "resourceType": "Patient", "id": "p1" }""");

        await Assert.ThrowsAsync<FhirRequestException>(
            () => client.SearchPatientsAsync(new PatientSearchCriteria(Name: "x")));
    }

    [Fact]
    public async Task A_response_that_is_not_json_is_rejected()
    {
        var (client, _) = Create("<html>Service unavailable</html>");

        await Assert.ThrowsAsync<FhirRequestException>(
            () => client.SearchPatientsAsync(new PatientSearchCriteria(Name: "x")));
    }
}
