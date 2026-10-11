namespace FhirMcp.Core.Privacy;

/// <summary>How much identifying information tool responses may carry.</summary>
public enum PrivacyMode
{
    /// <summary>Responses include names, cities and full dates.</summary>
    Full,

    /// <summary>
    /// Responses follow the HIPAA Safe Harbor approach: names and cities are removed,
    /// dates are reduced to the year, and ages over 89 are reported as "90 or older".
    /// </summary>
    Deidentified,
}

/// <summary>
/// The server's privacy and access policy, read once at startup from environment variables:
/// <list type="bullet">
/// <item><c>FHIR_MCP_PRIVACY</c>: <c>full</c> (default) or <c>deidentified</c>.</item>
/// <item><c>FHIR_MCP_AUDIT_LOG</c>: a file path for the audit log, or <c>off</c>. Defaults to <c>~/.fhir-mcp/audit.jsonl</c>.</item>
/// <item><c>FHIR_MCP_TOOLS</c>: a comma-separated list of tools to enable. Empty or unset enables all.</item>
/// </list>
/// </summary>
/// <param name="EnabledTools">The tools Claude may see and call; null means all.</param>
/// <param name="AuditLogPath">Where audit records are written; null means auditing is off.</param>
public sealed record PrivacyPolicy(PrivacyMode Mode, string? AuditLogPath, IReadOnlySet<string>? EnabledTools)
{
    public const string PrivacyVariable = "FHIR_MCP_PRIVACY";
    public const string AuditLogVariable = "FHIR_MCP_AUDIT_LOG";
    public const string ToolsVariable = "FHIR_MCP_TOOLS";

    /// <summary>Full detail, auditing to the default path, every tool enabled.</summary>
    public static PrivacyPolicy Default(string homeDirectory) =>
        new(PrivacyMode.Full, DefaultAuditPath(homeDirectory), null);

    public static string DefaultAuditPath(string homeDirectory) =>
        Path.Combine(homeDirectory, ".fhir-mcp", "audit.jsonl");

    public bool IsToolEnabled(string toolName) => EnabledTools is null || EnabledTools.Contains(toolName);

    /// <summary>
    /// Reads the policy. Invalid values stop the server at startup with a clear message,
    /// rather than silently falling back to a less restrictive setting.
    /// </summary>
    /// <param name="knownTools">Every tool the server offers, used to reject misspelled tool names.</param>
    public static PrivacyPolicy FromEnvironment(
        Func<string, string?> getVariable, string homeDirectory, IReadOnlyCollection<string> knownTools)
    {
        var modeText = getVariable(PrivacyVariable)?.Trim().ToLowerInvariant();
        var mode = modeText switch
        {
            null or "" or "full" => PrivacyMode.Full,
            "deidentified" or "de-identified" => PrivacyMode.Deidentified,
            _ => throw new ArgumentException(
                $"{PrivacyVariable} must be \"full\" or \"deidentified\". Got \"{getVariable(PrivacyVariable)}\"."),
        };

        var auditText = getVariable(AuditLogVariable)?.Trim();
        var auditPath = auditText switch
        {
            null or "" => DefaultAuditPath(homeDirectory),
            _ when auditText.Equals("off", StringComparison.OrdinalIgnoreCase) => null,
            _ when auditText.StartsWith("~/", StringComparison.Ordinal) => Path.Combine(homeDirectory, auditText[2..]),
            _ => auditText,
        };

        IReadOnlySet<string>? enabled = null;
        var toolsText = getVariable(ToolsVariable);
        if (!string.IsNullOrWhiteSpace(toolsText))
        {
            var requested = toolsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
            var unknown = requested.Where(t => !knownTools.Contains(t)).Order().ToList();
            if (unknown.Count > 0)
            {
                throw new ArgumentException(
                    $"{ToolsVariable} names unknown tools: {string.Join(", ", unknown)}. Known tools: {string.Join(", ", knownTools.Order())}.");
            }
            enabled = requested;
        }

        return new PrivacyPolicy(mode, auditPath, enabled);
    }

    /// <summary>One line for the startup log, so the active policy is visible in the client's server logs.</summary>
    public string Describe() =>
        $"privacy={Mode.ToString().ToLowerInvariant()}; audit={(AuditLogPath ?? "off")}; tools={(EnabledTools is null ? "all" : string.Join(",", EnabledTools.Order()))}";
}
