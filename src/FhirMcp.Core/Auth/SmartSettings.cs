namespace FhirMcp.Core.Auth;

/// <summary>Where sign-in tokens are kept between runs.</summary>
public enum TokenStoreKind
{
    /// <summary>A file encrypted with a key kept in the macOS Keychain.</summary>
    Keychain,

    /// <summary>A JSON file readable only by the current user.</summary>
    File,
}

/// <summary>
/// SMART on FHIR sign-in settings, read once at startup from environment variables:
/// <list type="bullet">
/// <item><c>FHIR_MCP_AUTH</c>: <c>none</c> (default) or <c>smart</c>.</item>
/// <item><c>FHIR_MCP_SMART_CLIENT_ID</c>: the client id sent to the authorization server. Defaults to <c>fhir-mcp</c>.</item>
/// <item><c>FHIR_MCP_SMART_SCOPE</c>: the scopes requested. Only read scopes are accepted.</item>
/// <item><c>FHIR_MCP_TOKEN_STORE</c>: <c>keychain</c> (an encrypted file whose key is in the macOS Keychain) or
/// <c>file</c> (plain JSON, owner-only permissions). Defaults to <c>keychain</c> on macOS and <c>file</c> elsewhere.</item>
/// </list>
/// </summary>
public sealed record SmartSettings(bool Enabled, string ClientId, string Scope, TokenStoreKind Store, string TokenFilePath)
{
    public const string AuthVariable = "FHIR_MCP_AUTH";
    public const string ClientIdVariable = "FHIR_MCP_SMART_CLIENT_ID";
    public const string ScopeVariable = "FHIR_MCP_SMART_SCOPE";
    public const string StoreVariable = "FHIR_MCP_TOKEN_STORE";

    public const string DefaultClientId = "fhir-mcp";

    /// <summary>
    /// Signs in as a user and reads what that user may read. <c>offline_access</c> asks for a
    /// refresh token, so the server can renew access without another browser sign-in.
    /// </summary>
    public const string DefaultScope = "openid fhirUser offline_access user/*.read";

    public static string DefaultTokenFilePath(string homeDirectory, TokenStoreKind store) =>
        Path.Combine(homeDirectory, ".fhir-mcp", store == TokenStoreKind.Keychain ? "tokens.enc" : "tokens.json");

    /// <summary>The token store these settings name.</summary>
    public ITokenStore CreateStore(IProcessRunner? runner = null) => Store == TokenStoreKind.Keychain
        ? new KeychainTokenStore(TokenFilePath, runner)
        : new FileTokenStore(TokenFilePath);

    /// <summary>
    /// Reads the settings. Invalid values, including any scope that would allow writing,
    /// stop the server at startup with a clear message.
    /// </summary>
    /// <param name="defaultEnabled">Sign-in when FHIR_MCP_AUTH is unset; true for a server preset that expects it.</param>
    /// <param name="defaultScope">The scope when FHIR_MCP_SMART_SCOPE is unset; a server preset may suggest one.</param>
    public static SmartSettings FromEnvironment(
        Func<string, string?> getVariable, string homeDirectory, bool isMacOS, bool defaultEnabled = false, string? defaultScope = null)
    {
        var authText = getVariable(AuthVariable)?.Trim().ToLowerInvariant();
        var enabled = authText switch
        {
            null or "" => defaultEnabled,
            "none" => false,
            "smart" => true,
            _ => throw new ArgumentException($"{AuthVariable} must be \"none\" or \"smart\". Got \"{getVariable(AuthVariable)}\"."),
        };

        var clientId = getVariable(ClientIdVariable)?.Trim();
        if (string.IsNullOrEmpty(clientId)) clientId = DefaultClientId;

        var scope = getVariable(ScopeVariable)?.Trim();
        if (string.IsNullOrEmpty(scope)) scope = defaultScope ?? DefaultScope;
        ValidateReadOnlyScope(scope);

        var storeText = getVariable(StoreVariable)?.Trim().ToLowerInvariant();
        var store = storeText switch
        {
            null or "" => isMacOS ? TokenStoreKind.Keychain : TokenStoreKind.File,
            "keychain" when isMacOS => TokenStoreKind.Keychain,
            "keychain" => throw new ArgumentException($"{StoreVariable}=keychain works only on macOS. Use \"file\"."),
            "file" => TokenStoreKind.File,
            _ => throw new ArgumentException($"{StoreVariable} must be \"keychain\" or \"file\". Got \"{getVariable(StoreVariable)}\"."),
        };

        return new SmartSettings(enabled, clientId, NormalizeScope(scope), store, DefaultTokenFilePath(homeDirectory, store));
    }

    /// <summary>
    /// The server is read-only (decision 0001), so a scope that grants writing is refused rather
    /// than requested and ignored. Accepts SMART v1 (<c>patient/*.read</c>) and v2
    /// (<c>patient/*.rs</c>) resource scopes.
    /// </summary>
    public static void ValidateReadOnlyScope(string scope)
    {
        foreach (var item in scope.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!item.Contains('/') || item.StartsWith("launch/", StringComparison.Ordinal))
            {
                continue; // openid, fhirUser, offline_access, launch/patient and the like
            }

            var dot = item.LastIndexOf('.');
            var access = dot < 0 ? "" : item[(dot + 1)..];
            var readOnly = access == "read" || (access.Length > 0 && access.All(c => c is 'r' or 's'));
            if (!readOnly)
            {
                throw new ArgumentException(
                    $"{ScopeVariable} asks for \"{item}\", which allows more than reading. This server is read-only; use .read (or .rs) scopes.");
            }
        }
    }

    private static string NormalizeScope(string scope) =>
        string.Join(' ', scope.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public string Describe() =>
        Enabled ? $"auth=smart; client={ClientId}; scope=\"{Scope}\"; tokens={Store.ToString().ToLowerInvariant()}" : "auth=none";
}
