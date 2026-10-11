using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FhirMcp.Core.Fhir;
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

    /// <param name="currentUser">Who is signed in to the FHIR server, for audit records; null without SMART sign-in.</param>
    public static IMcpServerBuilder WithPolicy(
        this IMcpServerBuilder builder, PrivacyPolicy policy, AuditLog? audit, Func<string?>? currentUser = null) =>
        builder.WithRequestFilters(filters =>
        {
            // A refusal because the session is limited to another patient counts as "denied", like a
            // disabled tool, so audit queries for refused access find both.
            static string OutcomeOf(string? error) =>
                error?.Contains(PatientAccessDeniedException.MessagePrefix, StringComparison.Ordinal) == true ? "denied" : "error";

            string? User()
            {
                try
                {
                    return currentUser?.Invoke();
                }
                catch (Exception)
                {
                    return null; // Never let reading the user break a call or its audit record.
                }
            }

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
                    audit?.Write(tool, "denied", clock.Elapsed, arguments, null, policy.Mode, client, message, User());
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
                    var outcome = failed ? OutcomeOf(text) : "ok";
                    audit?.Write(tool, outcome, clock.Elapsed, arguments,
                        failed ? null : text, policy.Mode, client, failed ? text : null, User());
                    return result;
                }
                catch (Exception ex)
                {
                    audit?.Write(tool, OutcomeOf(ex.Message), clock.Elapsed, arguments, null, policy.Mode, client, ex.Message, User());
                    throw;
                }
            });
        });
}
