using System.Net;
using System.Text;
using System.Text.Json;
using FhirMcp.Core.Auth;
using FhirMcp.Core.Fhir;
using FhirMcp.Core.Privacy;
using Xunit;

namespace FhirMcp.Tests;

/// <summary>
/// A fake authorization and FHIR server: answers each request from a function of the method,
/// URL and body, and records them, so sign-in can be tested without a network.
/// </summary>
internal sealed class FakeServer : HttpMessageHandler
{
    private readonly Func<HttpMethod, Uri, string, (HttpStatusCode Status, string Body)> _respond;

    public FakeServer(Func<HttpMethod, Uri, string, (HttpStatusCode Status, string Body)> respond) => _respond = respond;

    public List<(HttpMethod Method, Uri Url, string Body, string? Authorization)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString()));
        var (status, response) = _respond(request.Method, request.RequestUri!, body);
        return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
    }

    public static Dictionary<string, string> Form(string body) =>
        body.Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
}

/// <summary>An in-memory stand-in for the macOS <c>security</c> tool.</summary>
internal sealed class FakeKeychain : IProcessRunner
{
    public Dictionary<string, string> Items { get; } = new();

    public List<(IReadOnlyList<string> Arguments, string? Input)> Calls { get; } = new();

    public (int ExitCode, string StandardOutput) Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null)
    {
        Calls.Add((arguments, standardInput));
        if (arguments[0] == "find-generic-password")
        {
            var account = arguments[arguments.ToList().IndexOf("-a") + 1];
            return Items.TryGetValue(account, out var value) ? (0, value + "\n") : (44, "");
        }
        if (arguments[0] == "-i" && standardInput is not null)
        {
            var words = standardInput.Trim().Split(' ');
            Items[words[Array.IndexOf(words, "-a") + 1]] = words[Array.IndexOf(words, "-w") + 1];
            return (0, "");
        }
        return (1, "");
    }
}

internal static class Jwt
{
    /// <summary>An unsigned JWT with the given claims, for ID token tests.</summary>
    public static string With(object claims)
    {
        static string Part(object o) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(o))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Part(new { alg = "none" })}.{Part(claims)}.sig";
    }
}

public class SmartSettingsTests
{
    private static SmartSettings Read(bool mac = true, params (string Key, string Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Key, v => v.Value);
        return SmartSettings.FromEnvironment(k => map.GetValueOrDefault(k), "/home/test", mac);
    }

    [Fact]
    public void Off_by_default_with_read_only_user_scopes()
    {
        var settings = Read();

        Assert.False(settings.Enabled);
        Assert.Equal("fhir-mcp", settings.ClientId);
        Assert.Equal("openid fhirUser offline_access user/*.read", settings.Scope);
        Assert.Equal("auth=none", settings.Describe());
    }

    [Fact]
    public void Keeps_tokens_in_the_keychain_on_macOS_and_in_a_file_elsewhere()
    {
        Assert.Equal(TokenStoreKind.Keychain, Read(mac: true).Store);
        Assert.EndsWith("tokens.enc", Read(mac: true).TokenFilePath);
        Assert.Equal(TokenStoreKind.File, Read(mac: false).Store);
        Assert.EndsWith("tokens.json", Read(mac: false).TokenFilePath);
        Assert.Throws<ArgumentException>(() => Read(false, (SmartSettings.StoreVariable, "keychain")));
    }

    [Fact]
    public void Reads_smart_sign_in()
    {
        var settings = Read(true, (SmartSettings.AuthVariable, "SMART"), (SmartSettings.ScopeVariable, "  launch/patient   patient/*.read "));

        Assert.True(settings.Enabled);
        Assert.Equal("launch/patient patient/*.read", settings.Scope);
    }

    [Fact]
    public void Refuses_an_unknown_auth_mode()
    {
        var ex = Assert.Throws<ArgumentException>(() => Read(true, (SmartSettings.AuthVariable, "oauth")));
        Assert.Contains("\"none\" or \"smart\"", ex.Message);
    }

    [Theory]
    [InlineData("user/*.write")]
    [InlineData("patient/*.*")]
    [InlineData("patient/Observation.cruds")]
    [InlineData("openid user/Patient.read system/*.write")]
    public void Refuses_scopes_that_allow_writing(string scope)
    {
        var ex = Assert.Throws<ArgumentException>(() => SmartSettings.ValidateReadOnlyScope(scope));
        Assert.Contains("read-only", ex.Message);
    }

    [Theory]
    [InlineData("openid fhirUser offline_access user/*.read")]
    [InlineData("launch/patient patient/*.rs")]
    [InlineData("patient/Observation.r patient/Patient.read")]
    public void Accepts_read_scopes(string scope) => SmartSettings.ValidateReadOnlyScope(scope);
}

public class SmartAuthClientTests
{
    private const string Base = "https://ehr.example/fhir/";
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly SmartConfiguration Config =
        new("https://ehr.example/auth/authorize", "https://ehr.example/auth/token", ["S256"]);

    private static (SmartAuthClient Client, FakeServer Server) Create(Func<HttpMethod, Uri, string, (HttpStatusCode, string)> respond)
    {
        var server = new FakeServer(respond);
        return (new SmartAuthClient(new HttpClient(server), new FixedTimeAt(Now)), server);
    }

    [Fact]
    public void Pkce_challenge_matches_the_RFC_7636_example()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            PkcePair.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

        var a = PkcePair.Create();
        Assert.NotEqual(a.Verifier, PkcePair.Create().Verifier);
        Assert.Equal(43, a.Verifier.Length);
    }

    [Fact]
    public async Task Discovery_reads_the_endpoints()
    {
        var (client, server) = Create((_, _, _) => (HttpStatusCode.OK, """
            { "authorization_endpoint": "https://ehr.example/auth/authorize",
              "token_endpoint": "https://ehr.example/auth/token",
              "code_challenge_methods_supported": ["S256"] }
            """));

        var config = await client.DiscoverAsync(Base);

        Assert.Equal("https://ehr.example/fhir/.well-known/smart-configuration", server.Requests.Single().Url.AbsoluteUri);
        Assert.Equal("https://ehr.example/auth/token", config.TokenEndpoint);
    }

    [Theory]
    [InlineData("""{ "authorization_endpoint": "https://a/x", "token_endpoint": "https://a/t", "code_challenge_methods_supported": ["plain"] }""", "PKCE")]
    [InlineData("""{ "authorization_endpoint": "https://a/x", "token_endpoint": "http://a/t", "code_challenge_methods_supported": ["S256"] }""", "HTTPS")]
    [InlineData("""{ "token_endpoint": "https://a/t", "code_challenge_methods_supported": ["S256"] }""", "authorization_endpoint")]
    public async Task Discovery_refuses_unsafe_configurations(string json, string expected)
    {
        var (client, _) = Create((_, _, _) => (HttpStatusCode.OK, json));
        var ex = await Assert.ThrowsAsync<SmartAuthException>(() => client.DiscoverAsync(Base));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Authorize_url_carries_PKCE_state_and_the_audience()
    {
        var pkce = new PkcePair("verifier", "challenge");
        var url = new Uri(SmartAuthClient.BuildAuthorizeUrl(Config, "fhir-mcp", "http://127.0.0.1:5000/callback", "openid user/*.read", "s1", pkce, Base));
        var query = FakeServer.Form(url.Query.TrimStart('?'));

        Assert.Equal("code", query["response_type"]);
        Assert.Equal("challenge", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("s1", query["state"]);
        Assert.Equal("openid user/*.read", query["scope"]);
        Assert.Equal("https://ehr.example/fhir", query["aud"]); // no trailing slash
        Assert.DoesNotContain("verifier", url.AbsoluteUri);
    }

    [Fact]
    public void Callback_returns_the_code_only_when_the_state_matches()
    {
        Assert.Equal("abc", SmartAuthClient.ReadCallback("?code=abc&state=s1", "s1"));

        var forged = Assert.Throws<SmartAuthException>(() => SmartAuthClient.ReadCallback("?code=abc&state=other", "s1"));
        Assert.Contains("state", forged.Message);

        var denied = Assert.Throws<SmartAuthException>(() =>
            SmartAuthClient.ReadCallback("?error=access_denied&error_description=User+said+no&state=s1", "s1"));
        Assert.Contains("access_denied (User said no)", denied.Message);
    }

    [Fact]
    public async Task Code_exchange_sends_the_verifier_and_reads_the_tokens()
    {
        var idToken = Jwt.With(new { sub = "u1", fhirUser = "Practitioner/smart-1" });
        var (client, server) = Create((_, _, _) => (HttpStatusCode.OK, $$"""
            { "access_token": "at-1", "token_type": "bearer", "expires_in": 3600, "refresh_token": "rt-1",
              "scope": "openid fhirUser launch/patient patient/*.read", "patient": "p-1", "id_token": "{{idToken}}" }
            """));

        var tokens = await client.ExchangeCodeAsync(Config, "fhir-mcp", "http://127.0.0.1:5000/callback", "code-1", new PkcePair("v-1", "c-1"), Base);

        var form = FakeServer.Form(server.Requests.Single().Body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("code-1", form["code"]);
        Assert.Equal("v-1", form["code_verifier"]);
        Assert.Equal("fhir-mcp", form["client_id"]);
        Assert.False(form.ContainsKey("client_secret"));

        Assert.Equal("at-1", tokens.AccessToken);
        Assert.Equal("rt-1", tokens.RefreshToken);
        Assert.Equal(Now.AddHours(1), tokens.ExpiresAt);
        Assert.Equal("p-1", tokens.Patient);
        Assert.Equal("Practitioner/smart-1", tokens.FhirUser);
        Assert.True(tokens.IsLimitedToPatient);
    }

    [Fact]
    public async Task Refresh_keeps_what_the_response_leaves_out()
    {
        var old = new SmartTokens(Base, Config.TokenEndpoint, "fhir-mcp", "at-1", "rt-1", Now, "patient/*.read", "p-1", "Patient/p-1");
        var (client, server) = Create((_, _, _) => (HttpStatusCode.OK, """{ "access_token": "at-2", "token_type": "Bearer", "expires_in": 60 }"""));

        var tokens = await client.RefreshAsync(old);

        var form = FakeServer.Form(server.Requests.Single().Body);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("rt-1", form["refresh_token"]);
        Assert.Equal(("at-2", "rt-1", "p-1", "Patient/p-1", "patient/*.read"),
            (tokens.AccessToken, tokens.RefreshToken, tokens.Patient, tokens.FhirUser, tokens.Scope));
    }

    [Fact]
    public async Task A_refused_token_request_reports_the_OAuth_error()
    {
        var (client, _) = Create((_, _, _) => (HttpStatusCode.BadRequest, """{ "error": "invalid_grant", "error_description": "expired" }"""));
        var old = new SmartTokens(Base, Config.TokenEndpoint, "fhir-mcp", "at-1", "rt-1", Now, "", null, null);

        var ex = await Assert.ThrowsAsync<SmartAuthException>(() => client.RefreshAsync(old));
        Assert.Contains("invalid_grant, expired", ex.Message);
    }

    [Theory]
    [InlineData("launch/patient patient/*.read", "p-1", true)]
    [InlineData("launch/patient patient/*.read user/*.read", "p-1", false)]
    [InlineData("user/*.read", "p-1", false)]
    [InlineData("patient/*.read", null, false)]
    public void Only_patient_scopes_with_a_patient_limit_the_session(string scope, string? patient, bool limited)
    {
        var tokens = new SmartTokens(Base, "t", "c", "a", null, Now, scope, patient, null);
        Assert.Equal(limited, tokens.IsLimitedToPatient);
    }
}

public class TokenStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fhir-mcp-tests-" + Guid.NewGuid().ToString("N"));

    private static SmartTokens Tokens(string baseUrl = "https://ehr.example/fhir/", string access = "secret-access-token") =>
        new(baseUrl, "https://ehr.example/token", "fhir-mcp", access, "secret-refresh", DateTimeOffset.UnixEpoch, "user/*.read", null, "Practitioner/1");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void File_store_saves_one_sign_in_per_server_readable_only_by_the_owner()
    {
        var path = Path.Combine(_folder, "tokens.json");
        var store = new FileTokenStore(path);

        store.Save(Tokens());
        store.Save(Tokens("https://other.example/fhir", "other-token"));

        Assert.Equal("secret-access-token", store.Load("https://ehr.example/fhir")!.AccessToken); // slash or not
        Assert.Equal("other-token", store.Load("https://other.example/fhir/")!.AccessToken);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }

        Assert.True(store.Delete("https://ehr.example/fhir/"));
        Assert.False(store.Delete("https://ehr.example/fhir/"));
        Assert.Null(store.Load("https://ehr.example/fhir/"));
        Assert.NotNull(store.Load("https://other.example/fhir/"));
    }

    [Fact]
    public void A_damaged_file_means_signed_out()
    {
        var path = Path.Combine(_folder, "tokens.json");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(path, "{ not json");

        Assert.Null(new FileTokenStore(path).Load("https://ehr.example/fhir/"));
    }

    [Fact]
    public void Keychain_store_encrypts_the_file_with_a_key_passed_on_standard_input()
    {
        var path = Path.Combine(_folder, "tokens.enc");
        var keychain = new FakeKeychain();
        var store = new KeychainTokenStore(path, keychain);

        store.Save(Tokens());

        Assert.Equal("secret-access-token", new KeychainTokenStore(path, keychain).Load("https://ehr.example/fhir/")!.AccessToken);
        Assert.DoesNotContain("secret", Encoding.UTF8.GetString(File.ReadAllBytes(path)));

        var key = keychain.Items[KeychainKeyProtector.Account];
        Assert.All(keychain.Calls, c => Assert.DoesNotContain(key, c.Arguments)); // never on the command line
        Assert.Contains(keychain.Calls, c => c.Input?.Contains(key) == true);
    }

    [Fact]
    public void Keychain_store_treats_a_lost_key_as_signed_out()
    {
        var path = Path.Combine(_folder, "tokens.enc");
        new KeychainTokenStore(path, new FakeKeychain()).Save(Tokens());

        Assert.Null(new KeychainTokenStore(path, new FakeKeychain()).Load("https://ehr.example/fhir/"));
    }
}

public class SmartSessionTests : IDisposable
{
    private const string Base = "https://ehr.example/fhir/";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fhir-mcp-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FixedTimeAt _time = new(new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private FileTokenStore Store() => new(Path.Combine(_folder, "tokens.json"));

    private SmartTokens Tokens(string access, TimeSpan expiresIn, string scope = "user/*.read", string? patient = null) =>
        new(Base, "https://ehr.example/token", "fhir-mcp", access, "rt-1", _time.GetUtcNow() + expiresIn, scope, patient, "Practitioner/1");

    private (SmartSession Session, FakeServer Server, FileTokenStore Store) Create(Func<HttpMethod, Uri, string, (HttpStatusCode, string)>? respond = null)
    {
        var server = new FakeServer(respond ?? ((_, _, _) => (HttpStatusCode.OK, """{ "access_token": "at-new", "token_type": "Bearer", "expires_in": 3600 }""")));
        var store = Store();
        var session = new SmartSession(store, new SmartAuthClient(new HttpClient(server), _time), Base, "Run login.", _time);
        return (session, server, store);
    }

    [Fact]
    public async Task Asks_for_a_sign_in_when_there_is_none()
    {
        var (session, _, _) = Create();

        var ex = await Assert.ThrowsAsync<SmartAuthException>(() => session.GetAccessTokenAsync());
        Assert.Equal("Not signed in to the FHIR server. Run login.", ex.Message);
    }

    [Fact]
    public async Task Picks_up_a_sign_in_made_while_running()
    {
        var (session, _, store) = Create();
        await Assert.ThrowsAsync<SmartAuthException>(() => session.GetAccessTokenAsync());

        store.Save(Tokens("at-1", TimeSpan.FromHours(1)));

        Assert.Equal("at-1", await session.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Refreshes_shortly_before_expiry_and_saves_the_new_token()
    {
        var (session, server, store) = Create();
        store.Save(Tokens("at-1", TimeSpan.FromSeconds(30)));

        Assert.Equal("at-new", await session.GetAccessTokenAsync());
        Assert.Single(server.Requests);
        Assert.Equal("at-new", store.Load(Base)!.AccessToken);

        Assert.Equal("at-new", await session.GetAccessTokenAsync());
        Assert.Single(server.Requests); // still valid, no second refresh
    }

    [Fact]
    public async Task A_failed_refresh_asks_for_a_new_sign_in()
    {
        var (session, _, store) = Create((_, _, _) => (HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }"""));
        store.Save(Tokens("at-1", TimeSpan.Zero));

        var ex = await Assert.ThrowsAsync<SmartAuthException>(() => session.GetAccessTokenAsync());
        Assert.Contains("could not be renewed", ex.Message);
        Assert.EndsWith("Run login.", ex.Message);
    }

    [Fact]
    public async Task Bearer_handler_sends_the_token_and_retries_once_after_a_401()
    {
        var (session, authServer, store) = Create();
        store.Save(Tokens("at-old", TimeSpan.FromHours(1)));

        var calls = 0;
        var rejectFirst = new FakeServer((m, u, b) => ++calls == 1 ? (HttpStatusCode.Unauthorized, "") : (HttpStatusCode.OK, Fixtures.EmptyBundle));
        using var http = new HttpClient(new BearerTokenHandler(session, rejectFirst)) { BaseAddress = new Uri(Base) };

        using var response = await http.GetAsync("Patient?name=x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "Bearer at-old", "Bearer at-new" }, rejectFirst.Requests.Select(r => r.Authorization));
        Assert.Single(authServer.Requests);
    }

    [Fact]
    public void Limits_the_client_to_the_patient_chosen_at_sign_in()
    {
        var (session, _, store) = Create();
        Assert.Null(session.LockedPatientId);

        store.Save(Tokens("at-1", TimeSpan.FromHours(1), "launch/patient patient/*.read", "p-1"));
        Assert.Equal("p-1", session.LockedPatientId);
        Assert.Equal("Practitioner/1", session.User);
    }
}

public class PatientLimitTests
{
    private sealed class Locked(string id) : IAccessScope
    {
        public string? LockedPatientId => id;
    }

    private const string TwoPatients = """
        { "resourceType": "Bundle", "type": "searchset", "entry": [
          { "resource": { "resourceType": "Patient", "id": "p-1", "name": [ { "family": "Ziemann", "given": ["Reyna"] } ] } },
          { "resource": { "resourceType": "Patient", "id": "p-2", "name": [ { "family": "Ziemann", "given": ["Ona"] } ] } } ] }
        """;

    [Fact]
    public async Task Requests_about_another_patient_are_refused_before_anything_is_sent()
    {
        var handler = new FakeHandler(Fixtures.EmptyBundle);
        var client = new FhirApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://fhir.example/") }, access: new Locked("p-1"));

        var ex = await Assert.ThrowsAsync<PatientAccessDeniedException>(() => client.GetConditionsAsync("p-2"));

        Assert.StartsWith(PatientAccessDeniedException.MessagePrefix, ex.Message);
        Assert.Empty(handler.Requests);

        await client.GetConditionsAsync("p-1");
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Search_asks_only_about_the_allowed_patient_and_drops_any_other()
    {
        var handler = new FakeHandler(TwoPatients);
        var client = new FhirApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://fhir.example/") }, access: new Locked("p-1"));

        var result = await client.SearchPatientsAsync(new PatientSearchCriteria("ziemann", null, 10, false));

        Assert.Contains("_id=p-1", handler.Requests.Single().Query);
        Assert.Equal("p-1", Assert.Single(result.Patients).Id);
    }

    [Fact]
    public void Audit_records_name_the_signed_in_user()
    {
        var path = Path.Combine(Path.GetTempPath(), "fhir-mcp-audit-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var record = new AuditLog(path).Write("get_conditions", "denied", TimeSpan.Zero, null, null, PrivacyMode.Full, null, "Access denied: ...", "Practitioner/1");

            Assert.Equal("Practitioner/1", record.User);
            Assert.Contains("\"user\":\"Practitioner/1\"", File.ReadAllText(path));
            Assert.Null(AuditLog.Verify(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>A clock pinned to an exact instant.</summary>
internal sealed class FixedTimeAt(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
