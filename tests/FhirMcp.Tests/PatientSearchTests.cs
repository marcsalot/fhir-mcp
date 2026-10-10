using System.Net;
using FhirMcp.Core.Fhir;
using FhirMcp.Core.Models;
using Xunit;

namespace FhirMcp.Tests;

/// <summary>Mapping, request shape, limits and error handling. Searches here use a birth date, so no name filtering applies.</summary>
public class PatientSearchTests
{
    private static readonly DateOnly AnyDate = new(1980, 6, 15);
    private static readonly string Bundle = Fixtures.Read("patient-search-bundle.json");

    internal static (FhirApiClient Client, FakeHandler Handler) Create(
        FakeHandler handler,
        TimeProvider? time = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.org/fhir/") };
        return (new FhirApiClient(http, time ?? new FixedTime(2026, 6, 14)), handler);
    }

    private static (FhirApiClient Client, FakeHandler Handler) Create(
        string body,
        HttpStatusCode status = HttpStatusCode.OK,
        TimeProvider? time = null) =>
        Create(new FakeHandler(body, status), time);

    private static async Task<IReadOnlyList<PatientSummary>> SearchByDate(FhirApiClient client, int max = 10) =>
        (await client.SearchPatientsAsync(new PatientSearchCriteria(BirthDate: AnyDate, MaxResults: max))).Patients;

    [Fact]
    public async Task Returns_only_patients_from_the_bundle()
    {
        var (client, _) = Create(Bundle);

        var result = await SearchByDate(client);

        Assert.Equal(new[] { "pat-001", "pat-002", "pat-003" }, result.Select(p => p.Id).ToArray());
    }

    [Fact]
    public async Task Prefers_the_official_name_and_lists_the_others()
    {
        var (client, _) = Create(Bundle);

        var lopez = (await SearchByDate(client))[0];

        Assert.Equal("Maria Elena Lopez", lopez.Name);
        Assert.Equal(new[] { "Mari Lopez (usual)" }, lopez.OtherNames.ToArray());
    }

    [Fact]
    public async Task Uses_the_name_text_when_present()
    {
        var (client, _) = Create(Bundle);

        var smith = (await SearchByDate(client))[1];

        Assert.Equal("John Q. Smith", smith.Name);
        Assert.Empty(smith.OtherNames);
    }

    [Fact]
    public async Task Computes_age_the_day_before_and_on_the_birthday()
    {
        var (before, _) = Create(Bundle, time: new FixedTime(2026, 6, 14));
        var (onDay, _) = Create(Bundle, time: new FixedTime(2026, 6, 15));

        Assert.Equal(45, (await SearchByDate(before))[0].Age);
        Assert.Equal(46, (await SearchByDate(onDay))[0].Age);
    }

    [Fact]
    public async Task Partial_birth_dates_give_no_birth_date_or_age()
    {
        var (client, _) = Create(Bundle);

        var smith = (await SearchByDate(client))[1];

        Assert.Null(smith.BirthDate);
        Assert.Null(smith.Age);
    }

    [Fact]
    public async Task Deceased_patients_are_flagged_and_have_no_age()
    {
        var (client, _) = Create(Bundle);

        var okafor = (await SearchByDate(client))[2];

        Assert.True(okafor.Deceased);
        Assert.Null(okafor.Age);
        Assert.Equal(new DateOnly(1940, 2, 29), okafor.BirthDate);
    }

    [Fact]
    public async Task Prefers_the_home_address_then_the_first()
    {
        var (client, _) = Create(Bundle);

        var result = await SearchByDate(client);

        Assert.Equal("Dallas", result[0].City);
        Assert.Null(result[1].City);
        Assert.Equal("Austin", result[2].City);
    }

    [Fact]
    public async Task A_birth_date_search_sends_only_the_date_and_asks_for_fhir_json()
    {
        var (client, handler) = Create(Bundle);

        var result = await client.SearchPatientsAsync(new PatientSearchCriteria(BirthDate: AnyDate, MaxResults: 5));

        Assert.Equal(
            "https://example.org/fhir/Patient?birthdate=1980-06-15&_count=5",
            handler.LastRequest!.RequestUri!.AbsoluteUri);
        Assert.Contains(handler.LastRequest.Headers.Accept, h => h.MediaType == "application/fhir+json");
        Assert.Equal(NameMatchMode.None, result.MatchMode);
    }

    [Fact]
    public async Task Caps_results_even_if_the_server_returns_more()
    {
        var (client, _) = Create(Bundle);

        Assert.Equal(2, (await SearchByDate(client, max: 2)).Count);
    }

    [Fact]
    public async Task An_empty_search_returns_an_empty_list()
    {
        var (client, _) = Create(Fixtures.EmptyBundle);

        Assert.Empty(await SearchByDate(client));
    }

    [Fact]
    public async Task Requires_a_name_or_birth_date()
    {
        var (client, handler) = Create(Bundle);

        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchPatientsAsync(new PatientSearchCriteria()));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchPatientsAsync(new PatientSearchCriteria(Name: " - . ")));
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
