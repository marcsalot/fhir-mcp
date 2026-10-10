using System.Net;
using FhirMcp.Core.Fhir;
using FhirMcp.Core.Matching;
using FhirMcp.Core.Models;
using Xunit;

namespace FhirMcp.Tests;

/// <summary>
/// How typed names are matched. The fake server returns the same six candidates to every
/// starts-with query (as a broad server match would), so these tests exercise the matching
/// done on our side. Contains queries return the same candidates unless a test says otherwise.
/// </summary>
public class NameSearchTests
{
    private static readonly string Candidates = Fixtures.Read("name-search-bundle.json");

    private static (FhirApiClient Client, FakeHandler Handler) Create(
        Func<Uri, (HttpStatusCode, string)>? respond = null) =>
        PatientSearchTests.Create(new FakeHandler(respond ?? (_ => (HttpStatusCode.OK, Candidates))));

    private static async Task<string[]> Ids(FhirApiClient client, string name, bool contains = false) =>
        (await client.SearchPatientsAsync(new PatientSearchCriteria(Name: name, Contains: contains)))
            .Patients.Select(p => p.Id).ToArray();

    [Theory]
    [InlineData("Al-Sayed")]
    [InlineData("al-sayed")]
    [InlineData("Al Sayed")]
    [InlineData("Sayed")]
    [InlineData("Fatima Al-Sayed")]
    [InlineData("Al-Sayed Fatima")]
    [InlineData("fatima sayed")]
    public async Task Hyphenated_family_names_match_however_they_are_typed(string typed)
    {
        var (client, _) = Create();

        Assert.Equal(new[] { "p-alsayed" }, await Ids(client, typed));
    }

    [Fact]
    public async Task A_full_name_matches_given_and_family_words()
    {
        var (client, _) = Create();

        Assert.Equal(new[] { "p-smith" }, await Ids(client, "Adolph Smith"));
    }

    [Fact]
    public async Task A_word_matches_the_start_of_names_not_the_middle()
    {
        var (client, _) = Create();

        Assert.Equal(new[] { "p-smith", "p-smitham" }, await Ids(client, "smith"));
        Assert.Equal(new[] { "p-williamson" }, await Ids(client, "ham"));
    }

    [Fact]
    public async Task Contains_mode_matches_inside_names()
    {
        var (client, _) = Create();

        Assert.Equal(new[] { "p-williamson", "p-smitham" }, await Ids(client, "ham", contains: true));
    }

    [Fact]
    public async Task Maiden_names_match_and_are_shown_as_other_names()
    {
        var (client, _) = Create();

        var result = await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "Melony Hammes"));

        var patient = Assert.Single(result.Patients);
        Assert.Equal("Melony Williamson", patient.Name);
        Assert.Equal(new[] { "Melony Hammes (maiden)" }, patient.OtherNames.ToArray());
    }

    [Theory]
    [InlineData("arisso")]
    [InlineData("ARISSÓ")]
    [InlineData("andres arisso-test")]
    public async Task Accents_and_case_are_ignored(string typed)
    {
        var (client, _) = Create();

        Assert.Equal(new[] { "p-arisso" }, await Ids(client, typed));
    }

    [Theory]
    [InlineData("O'Brien")]
    [InlineData("obrien")]
    [InlineData("Siobhan O Brien")]
    public async Task Apostrophes_are_ignored(string typed)
    {
        var (client, _) = Create();

        Assert.Equal(new[] { "p-obrien" }, await Ids(client, typed));
    }

    [Fact]
    public async Task The_server_is_asked_about_the_longest_word_with_hyphens_kept()
    {
        var (client, handler) = Create();

        await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "Fatima Al-Sayed", MaxResults: 5));

        Assert.Equal(
            "https://example.org/fhir/Patient?name=Al-Sayed&_count=100",
            handler.Requests[0].AbsoluteUri);
    }

    [Fact]
    public async Task A_single_word_asks_the_server_for_only_the_results_needed()
    {
        var (client, handler) = Create();

        await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "smith", MaxResults: 5));

        Assert.Equal("https://example.org/fhir/Patient?name=smith&_count=5", handler.Requests[0].AbsoluteUri);
    }

    [Fact]
    public async Task Falls_back_to_contains_when_starts_with_finds_nothing()
    {
        var (client, handler) = Create(uri => uri.AbsoluteUri.Contains("name:contains=")
            ? (HttpStatusCode.OK, Candidates)
            : (HttpStatusCode.OK, Fixtures.EmptyBundle));

        var result = await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "Sayed"));

        Assert.Equal(new[] { "p-alsayed" }, result.Patients.Select(p => p.Id).ToArray());
        Assert.Equal(NameMatchMode.Contains, result.MatchMode);
        Assert.True(result.FellBackToContains);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("name:contains=Sayed", handler.Requests[1].AbsoluteUri);
    }

    [Fact]
    public async Task Does_not_fall_back_when_starts_with_finds_someone()
    {
        var (client, handler) = Create();

        var result = await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "smith"));

        Assert.Equal(NameMatchMode.StartsWith, result.MatchMode);
        Assert.False(result.FellBackToContains);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Reports_when_the_server_has_more_candidates()
    {
        var (client, _) = Create();

        var result = await client.SearchPatientsAsync(new PatientSearchCriteria(Name: "smith"));

        Assert.True(result.MoreAvailable);
    }

    [Fact]
    public void Query_tokens_split_on_spaces_and_hyphens_and_drop_punctuation()
    {
        Assert.Equal(new[] { "al", "sayed", "obrien", "jr" }, NameMatcher.QueryTokens("Al-Sayed  O'Brien Jr.").ToArray());
    }
}
