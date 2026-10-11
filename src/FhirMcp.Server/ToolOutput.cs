using FhirMcp.Core.Privacy;

namespace FhirMcp.Server;

/// <summary>
/// Turns a tool's response into the JSON text the model receives, applying the server's
/// privacy mode. Every tool goes through this one place, so no tool can skip de-identification.
/// </summary>
public sealed class ToolOutput
{
    private readonly PrivacyMode _mode;

    public ToolOutput(PrivacyPolicy policy) => _mode = policy.Mode;

    public ToolOutput(PrivacyMode mode) => _mode = mode;

    public string Serialize<T>(T value)
    {
        var json = ToolSerializer.Serialize(value);
        return _mode == PrivacyMode.Deidentified ? Deidentifier.Apply(json) : json;
    }
}
