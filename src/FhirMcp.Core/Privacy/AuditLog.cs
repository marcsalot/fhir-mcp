using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FhirMcp.Core.Privacy;

/// <summary>One audited tool call. Written as a single JSON line.</summary>
/// <param name="Outcome">"ok", "error" (the tool ran and failed) or "denied" (blocked by policy).</param>
/// <param name="Arguments">The call's arguments, with names and birth dates replaced by "[redacted]".</param>
/// <param name="PatientIds">Every patient id the call asked about or returned: who was accessed.</param>
/// <param name="ItemCount">How many items the response held, when it reports a count.</param>
/// <param name="User">Who was signed in to the FHIR server (such as "Practitioner/123"), when SMART sign-in is on.</param>
/// <param name="Previous">SHA-256 of the previous line, so an edited or deleted line breaks the chain.</param>
public sealed record AuditRecord(
    DateTimeOffset Time,
    string Tool,
    string Outcome,
    long DurationMs,
    IReadOnlyDictionary<string, string?> Arguments,
    IReadOnlyList<string> PatientIds,
    int? ItemCount,
    string Privacy,
    string? Client,
    string? User,
    string? Error,
    string Previous);

/// <summary>
/// An append-only, tamper-evident audit log of tool calls, one JSON object per line.
///
/// The log records who was accessed (patient ids) and what kind of request was made, but not
/// the names or birth dates typed into a search, so the log does not become a second copy of
/// identifying data. Each line carries the SHA-256 hash of the line before it; editing or
/// removing any line breaks every hash after it, which <see cref="Verify"/> detects.
/// </summary>
public sealed class AuditLog
{
    /// <summary>The "previous" value of the first line in a new log.</summary>
    public const string Genesis = "genesis";

    private static readonly HashSet<string> SensitiveArguments = new(StringComparer.OrdinalIgnoreCase) { "name", "birthDate" };

    private static readonly JsonSerializerOptions LineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;

    public string FilePath => _path;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private string _previousHash;

    public AuditLog(string path, TimeProvider? time = null)
    {
        _path = path;
        _time = time ?? TimeProvider.System;

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Continue an existing chain where it left off.
        var lastLine = File.Exists(path) ? File.ReadLines(path).LastOrDefault(l => l.Length > 0) : null;
        _previousHash = lastLine is null ? Genesis : Hash(lastLine);
    }

    /// <summary>Builds a record for a finished tool call and appends it.</summary>
    /// <param name="arguments">The call's raw arguments.</param>
    /// <param name="responseJson">The tool's JSON response, or null when it failed or was denied.</param>
    public AuditRecord Write(
        string tool,
        string outcome,
        TimeSpan duration,
        IReadOnlyDictionary<string, JsonElement>? arguments,
        string? responseJson,
        PrivacyMode privacy,
        string? client,
        string? error,
        string? user = null)
    {
        lock (_gate)
        {
            var record = new AuditRecord(
                Time: _time.GetUtcNow(),
                Tool: tool,
                Outcome: outcome,
                DurationMs: (long)duration.TotalMilliseconds,
                Arguments: SanitizeArguments(arguments),
                PatientIds: PatientIdsIn(arguments, responseJson),
                ItemCount: CountIn(responseJson),
                Privacy: privacy == PrivacyMode.Deidentified ? "deidentified" : "full",
                Client: client,
                User: user,
                Error: error,
                Previous: _previousHash);

            var line = JsonSerializer.Serialize(record, LineOptions);
            File.AppendAllText(_path, line + "\n");
            _previousHash = Hash(line);
            return record;
        }
    }

    /// <summary>
    /// Checks the hash chain of a log file. Returns null when it is intact, or a description
    /// of the first line whose "previous" hash does not match the line before it.
    /// </summary>
    public static string? Verify(string path)
    {
        var expected = Genesis;
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (line.Length == 0)
            {
                continue;
            }

            string? previous;
            try
            {
                previous = JsonNode.Parse(line)?["previous"]?.GetValue<string>();
            }
            catch (JsonException)
            {
                return $"Line {number} is not valid JSON.";
            }

            if (previous != expected)
            {
                return $"Line {number} does not follow the line before it: the log was edited, reordered, or had lines removed at or before this point.";
            }

            expected = Hash(line);
        }

        return null;
    }

    public static string Hash(string line) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(line)));

    internal static IReadOnlyDictionary<string, string?> SanitizeArguments(IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        var result = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        if (arguments is null)
        {
            return result;
        }

        foreach (var (key, value) in arguments)
        {
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            result[key] = SensitiveArguments.Contains(key)
                ? "[redacted]"
                : value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }

        return result;
    }

    /// <summary>Patient ids from the "patientId" argument and from patient objects in the response.</summary>
    internal static IReadOnlyList<string> PatientIdsIn(IReadOnlyDictionary<string, JsonElement>? arguments, string? responseJson)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);

        if (arguments is not null && arguments.TryGetValue("patientId", out var argId)
            && argId.ValueKind == JsonValueKind.String && argId.GetString() is { Length: > 0 } a)
        {
            ids.Add(a);
        }

        if (responseJson is not null)
        {
            try
            {
                var root = JsonNode.Parse(responseJson);
                if (root?["patientId"] is JsonValue pid && pid.TryGetValue<string>(out var p)) ids.Add(p);
                if (root?["patient"]?["id"] is JsonValue single && single.TryGetValue<string>(out var s)) ids.Add(s);
                if (root?["patients"] is JsonArray many)
                {
                    foreach (var patient in many)
                    {
                        if (patient?["id"] is JsonValue id && id.TryGetValue<string>(out var i)) ids.Add(i);
                    }
                }
            }
            catch (JsonException)
            {
                // Not JSON (an error message); nothing more to extract.
            }
        }

        return ids.ToList();
    }

    internal static int? CountIn(string? responseJson)
    {
        if (responseJson is null)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(responseJson)?["count"] is JsonValue c && c.TryGetValue<int>(out var n) ? n : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
