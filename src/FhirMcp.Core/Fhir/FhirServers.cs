namespace FhirMcp.Core.Fhir;

/// <summary>A named test server, so switching servers means changing one word, not a long URL.</summary>
/// <param name="SignIn">True when the server expects SMART sign-in, which then becomes the default.</param>
/// <param name="DefaultScope">The scope to request at sign-in, when the user sets none.</param>
public sealed record FhirServerPreset(string Name, string BaseUrl, string Description, bool SignIn = false, string? DefaultScope = null);

/// <summary>
/// Which FHIR server to use, read from environment variables:
/// <list type="bullet">
/// <item><c>FHIR_MCP_SERVER</c>: a preset name (<c>smart</c>, <c>hapi</c> or <c>smart-login</c>).</item>
/// <item><c>FHIR_BASE_URL</c>: any other server's base URL.</item>
/// </list>
/// Setting both is refused, since it is unclear which one was meant. Setting neither uses <c>smart</c>.
/// </summary>
public sealed record FhirServerSettings(string BaseUrl, FhirServerPreset? Preset)
{
    public const string ServerVariable = "FHIR_MCP_SERVER";
    public const string BaseUrlVariable = "FHIR_BASE_URL";

    /// <summary>
    /// The SMART launcher's settings, encoded in its URL: a provider signing in on their own
    /// (standalone launch), choosing a practitioner and a patient, approving, with PKCE required.
    /// </summary>
    public const string SmartLauncherUrl =
        "https://launch.smarthealthit.org/v/r4/sim/WzIsIiIsIiIsIk5PTkUiLDAsMCwwLCIiLCIiLCIiLCIiLCIiLCIiLCIiLDAsMiwiIl0/fhir/";

    public static readonly IReadOnlyList<FhirServerPreset> Presets =
    [
        new("smart", "https://r4.smarthealthit.org/",
            "SMART Health IT sandbox: curated synthetic patients, no sign-in (default)"),
        new("hapi", "https://hapi.fhir.org/baseR4/",
            "HAPI public test server: edge cases such as hyphenated names; publicly writable and cleared from time to time"),
        new("smart-login", SmartLauncherUrl,
            "SMART launcher: the same patients as smart, behind a simulated sign-in (practitioner, patient picker, approval)",
            SignIn: true, DefaultScope: "openid fhirUser offline_access launch/patient patient/*.read"),
    ];

    public static FhirServerSettings FromEnvironment(Func<string, string?> getVariable)
    {
        var name = getVariable(ServerVariable)?.Trim().ToLowerInvariant();
        var url = getVariable(BaseUrlVariable)?.Trim();

        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
        {
            throw new ArgumentException(
                $"Set {ServerVariable} or {BaseUrlVariable}, not both. {ServerVariable} picks a test server by name; {BaseUrlVariable} is for any other server.");
        }

        if (!string.IsNullOrEmpty(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new ArgumentException($"{BaseUrlVariable} must be an http or https URL. Got \"{url}\".");
            }
            return new FhirServerSettings(url.EndsWith('/') ? url : url + "/", null);
        }

        var preset = string.IsNullOrEmpty(name)
            ? Presets[0]
            : Presets.FirstOrDefault(p => p.Name == name)
              ?? throw new ArgumentException(
                  $"{ServerVariable} must be one of: {string.Join(", ", Presets.Select(p => p.Name))}. Got \"{getVariable(ServerVariable)}\".");

        return new FhirServerSettings(preset.BaseUrl, preset);
    }

    /// <summary>For the startup log and the <c>servers</c> command.</summary>
    public string Describe() => Preset is null ? BaseUrl : $"{Preset.Name} ({BaseUrl})";
}
