using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FhirMcp.Core.Privacy;
using FhirMcp.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FhirMcp.Server;

/// <summary>
/// Enforces the privacy policy around every MCP request: hides disabled tools from the
/// tool list, refuses calls to them, and writes an audit record for every call.
/// Doing this in the request pipeline, not in each tool, means a new tool is covered
/// automatically.
/// </summary>
internal static class PolicyFilters
{
    /// <summary>Every tool name the server offers, read from the tool attributes.</summary>
    public static IReadOnlyList<string> KnownTools { get; } =
        new[] { typeof(PatientTools), typeof(ClinicalTools) }
            .SelectMany(t => t.GetMethods())
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => n is not null)
            .Select(n => n!)
            .Order()
            .ToList();

    public static IMcpServerBuilder WithPolicy(this IMcpServerBuilder builder, PrivacyPolicy policy, AuditLog? audit) =>
        builder.WithRequestFilters(filters =>
        {
            filters.AddListToolsFilter(next => async (context, cancellationToken) =>
            {
                var result = await next(context, cancellationToken);
                if (policy.EnabledTools is not null)
                {
                    result.Tools = result.Tools.Where(t => policy.IsToolEnabled(t.Name)).ToList();
                }
                return result;
            });

            filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                var tool = context.Params?.Name ?? "(unknown)";
                var arguments = context.Params?.Arguments as IReadOnlyDictionary<string, JsonElement>
                    ?? context.Params?.Arguments?.ToDictionary(kv => kv.Key, kv => kv.Value);
                var client = context.Server.ClientInfo?.Name;
                var clock = Stopwatch.StartNew();

                if (!policy.IsToolEnabled(tool))
                {
                    var message = $"The tool \"{tool}\" is disabled by this server's policy ({PrivacyPolicy.ToolsVariable}).";
                    audit?.Write(tool, "denied", clock.Elapsed, arguments, null, policy.Mode, client, message);
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = message }],
                    };
                }

                try
                {
                    var result = await next(context, cancellationToken);
                    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                    var failed = result.IsError == true;
                    audit?.Write(tool, failed ? "error" : "ok", clock.Elapsed, arguments,
                        failed ? null : text, policy.Mode, client, failed ? text : null);
                    return result;
                }
                catch (Exception ex)
                {
                    audit?.Write(tool, "error", clock.Elapsed, arguments, null, policy.Mode, client, ex.Message);
                    throw;
                }
            });
        });
}
