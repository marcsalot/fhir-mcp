using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FhirMcp.Core.Auth;

/// <summary>
/// What a successful sign-in leaves behind: the tokens plus what is needed to renew them
/// without another browser sign-in.
/// </summary>
/// <param name="FhirBaseUrl">The FHIR server these tokens are for (the audience).</param>
/// <param name="Scope">The scopes actually granted, which can be narrower than those requested.</param>
/// <param name="Patient">The patient in context, when the sign-in selected one (launch/patient).</param>
/// <param name="FhirUser">Who signed in, such as "Practitioner/123", read from the ID token.</param>
public sealed record SmartTokens(
    string FhirBaseUrl,
    string TokenEndpoint,
    string ClientId,
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string Scope,
    string? Patient,
    string? FhirUser)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>True when the access token expires within <paramref name="margin"/>.</summary>
    public bool ExpiresWithin(TimeSpan margin, DateTimeOffset now) => ExpiresAt - now <= margin;

    /// <summary>
    /// True when every resource scope granted is a patient scope. The session is then limited to
    /// the patient chosen at sign-in, and this server refuses requests about anyone else.
    /// </summary>
    public bool IsLimitedToPatient
    {
        get
        {
            var resourceScopes = Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => s.Contains('/') && !s.StartsWith("launch/", StringComparison.Ordinal))
                .ToList();
            return Patient is not null
                && resourceScopes.Count > 0
                && resourceScopes.All(s => s.StartsWith("patient/", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Reads an OAuth token response. <paramref name="previous"/> supplies the refresh token,
    /// patient and user when a refresh response leaves them out.
    /// </summary>
    public static SmartTokens FromTokenResponse(
        string json, string fhirBaseUrl, string tokenEndpoint, string clientId, DateTimeOffset now, SmartTokens? previous = null)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new SmartAuthException("The authorization server's token response is not valid JSON.", ex);
        }

        var accessToken = GetString(root, "access_token")
            ?? throw new SmartAuthException("The authorization server's token response has no access_token.");

        var tokenType = GetString(root, "token_type");
        if (tokenType is not null && !tokenType.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new SmartAuthException($"Expected a Bearer token but the authorization server issued \"{tokenType}\".");
        }

        // Without expires_in, assume a short life so the token is refreshed early rather than late.
        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 300;

        return new SmartTokens(
            FhirBaseUrl: fhirBaseUrl,
            TokenEndpoint: tokenEndpoint,
            ClientId: clientId,
            AccessToken: accessToken,
            RefreshToken: GetString(root, "refresh_token") ?? previous?.RefreshToken,
            ExpiresAt: now.AddSeconds(expiresIn),
            Scope: GetString(root, "scope") ?? previous?.Scope ?? "",
            Patient: GetString(root, "patient") ?? previous?.Patient,
            FhirUser: FhirUserFromIdToken(GetString(root, "id_token")) ?? previous?.FhirUser);
    }

    /// <summary>
    /// The fhirUser claim from an ID token. The signature is not checked: the token came straight
    /// from the token endpoint over HTTPS, and the claim is used only to label audit records,
    /// never to decide access.
    /// </summary>
    internal static string? FhirUserFromIdToken(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        try
        {
            var payload = Base64Url.Decode(parts[1]);
            using var doc = JsonDocument.Parse(payload);
            return GetString(doc.RootElement, "fhirUser");
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } s
            ? s
            : null;

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static SmartTokens? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SmartTokens>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A sign-in or token problem, with a message safe to show to the user and the model.</summary>
public sealed class SmartAuthException : Exception
{
    public SmartAuthException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }

    public static string DecodeToString(string text) => Encoding.UTF8.GetString(Decode(text));
}
