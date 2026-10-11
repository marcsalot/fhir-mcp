using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FhirMcp.Core.Auth;

/// <summary>The endpoints a FHIR server publishes at <c>.well-known/smart-configuration</c>.</summary>
public sealed record SmartConfiguration(string AuthorizationEndpoint, string TokenEndpoint, IReadOnlyList<string> CodeChallengeMethods);

/// <summary>A PKCE pair: the secret verifier kept here, and its hash sent with the sign-in request.</summary>
public sealed record PkcePair(string Verifier, string Challenge)
{
    /// <summary>A random 256-bit verifier and its S256 challenge (RFC 7636).</summary>
    public static PkcePair Create()
    {
        var verifier = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
        return new PkcePair(verifier, ChallengeFor(verifier));
    }

    public static string ChallengeFor(string verifier) =>
        Base64Url.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}

/// <summary>
/// SMART App Launch for a public client (no client secret): discovery, the authorization URL
/// with PKCE, the code exchange, and token refresh. The browser and the local redirect listener
/// live in the server project; everything here is plain HTTP so it can be tested offline.
/// </summary>
public sealed class SmartAuthClient
{
    private readonly HttpClient _http;
    private readonly TimeProvider _time;

    public SmartAuthClient(HttpClient http, TimeProvider? time = null)
    {
        _http = http;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Reads the server's SMART configuration and checks it supports PKCE with S256.</summary>
    public async Task<SmartConfiguration> DiscoverAsync(string fhirBaseUrl, CancellationToken cancellationToken = default)
    {
        var url = fhirBaseUrl.TrimEnd('/') + "/.well-known/smart-configuration";
        string body;
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new SmartAuthException(
                    $"The FHIR server does not publish a SMART configuration ({(int)response.StatusCode} from {url}). It may not support SMART sign-in.");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new SmartAuthException($"Could not reach {url}: {ex.Message}", ex);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var authorize = RequireEndpoint(root, "authorization_endpoint");
            var token = RequireEndpoint(root, "token_endpoint");
            var methods = root.TryGetProperty("code_challenge_methods_supported", out var m) && m.ValueKind == JsonValueKind.Array
                ? m.EnumerateArray().Select(x => x.GetString()).OfType<string>().ToList()
                : new List<string>();

            if (!methods.Contains("S256"))
            {
                throw new SmartAuthException(
                    "The authorization server does not list PKCE with S256. This client signs in only with PKCE, so it will not continue.");
            }

            return new SmartConfiguration(authorize, token, methods);
        }
        catch (JsonException ex)
        {
            throw new SmartAuthException("The SMART configuration is not valid JSON.", ex);
        }
    }

    /// <summary>The URL to open in the browser. <c>aud</c> names the FHIR server the token is for.</summary>
    public static string BuildAuthorizeUrl(
        SmartConfiguration config, string clientId, string redirectUri, string scope, string state, PkcePair pkce, string fhirBaseUrl)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = scope,
            ["state"] = state,
            ["aud"] = fhirBaseUrl.TrimEnd('/'),
            ["code_challenge"] = pkce.Challenge,
            ["code_challenge_method"] = "S256",
        };

        var separator = config.AuthorizationEndpoint.Contains('?') ? '&' : '?';
        return config.AuthorizationEndpoint + separator
            + string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }

    /// <summary>
    /// Reads the query string the browser brings back to the redirect URI. Returns the code, or
    /// throws if the server reported an error or the state does not match (a forged redirect).
    /// </summary>
    public static string ReadCallback(string query, string expectedState)
    {
        var values = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "");

        if (values.TryGetValue("error", out var error))
        {
            values.TryGetValue("error_description", out var description);
            throw new SmartAuthException($"Sign-in was not completed: {error}{(string.IsNullOrEmpty(description) ? "" : $" ({description})")}.");
        }

        if (!values.TryGetValue("state", out var state)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState)))
        {
            throw new SmartAuthException("The sign-in response did not match the request (state mismatch). Start the sign-in again.");
        }

        return values.TryGetValue("code", out var code) && code.Length > 0
            ? code
            : throw new SmartAuthException("The sign-in response had no authorization code.");
    }

    /// <summary>Trades the authorization code and PKCE verifier for tokens.</summary>
    public Task<SmartTokens> ExchangeCodeAsync(
        SmartConfiguration config, string clientId, string redirectUri, string code, PkcePair pkce, string fhirBaseUrl,
        CancellationToken cancellationToken = default) =>
        PostTokenAsync(config.TokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = pkce.Verifier,
        }, fhirBaseUrl, clientId, previous: null, cancellationToken);

    /// <summary>Gets a new access token with the refresh token, without involving the user.</summary>
    public Task<SmartTokens> RefreshAsync(SmartTokens tokens, CancellationToken cancellationToken = default)
    {
        if (tokens.RefreshToken is null)
        {
            throw new SmartAuthException("The sign-in has expired and there is no refresh token. Sign in again.");
        }

        return PostTokenAsync(tokens.TokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokens.RefreshToken,
            ["client_id"] = tokens.ClientId,
        }, tokens.FhirBaseUrl, tokens.ClientId, tokens, cancellationToken);
    }

    private async Task<SmartTokens> PostTokenAsync(
        string tokenEndpoint, Dictionary<string, string> form, string fhirBaseUrl, string clientId, SmartTokens? previous,
        CancellationToken cancellationToken)
    {
        string body;
        HttpStatusCode status;
        try
        {
            using var response = await _http.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
            status = response.StatusCode;
        }
        catch (HttpRequestException ex)
        {
            throw new SmartAuthException($"Could not reach the token endpoint: {ex.Message}", ex);
        }

        if ((int)status >= 400)
        {
            throw new SmartAuthException($"The authorization server refused the token request ({(int)status}){DescribeOAuthError(body)}. Sign in again.");
        }

        return SmartTokens.FromTokenResponse(body, fhirBaseUrl, tokenEndpoint, clientId, _time.GetUtcNow(), previous);
    }

    private static string DescribeOAuthError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            var description = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            return error is null ? "" : $": {error}{(description is null ? "" : $", {description}")}";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    /// <summary>Endpoints must use HTTPS, except on this machine (used by tests and local servers).</summary>
    private static string RequireEndpoint(JsonElement root, string name)
    {
        var value = root.TryGetProperty(name, out var v) ? v.GetString() : null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new SmartAuthException($"The SMART configuration has no valid {name}.");
        }
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
        {
            throw new SmartAuthException($"The SMART configuration's {name} does not use HTTPS ({value}).");
        }
        return uri.AbsoluteUri;
    }
}
