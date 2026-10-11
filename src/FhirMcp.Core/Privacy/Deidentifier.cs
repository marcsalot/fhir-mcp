using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FhirMcp.Core.Privacy;

/// <summary>
/// Removes identifying details from a tool response before it reaches the model, following
/// the HIPAA Safe Harbor approach for the fields this server returns:
/// <list type="bullet">
/// <item>Patient names and other names are replaced with "[redacted]".</item>
/// <item>Cities are removed; the state is kept, as Safe Harbor allows.</item>
/// <item>Every full date (birth, onset, visit, order, result) is reduced to its year.</item>
/// <item>Ages over 89 are reported as "90 or older".</item>
/// </list>
/// Patient ids are kept, because the model needs them to call the other tools. They are
/// opaque server ids, not record numbers, but they still link back to a person on the
/// server, so this mode limits what the model sees rather than making the data anonymous.
/// </summary>
public static partial class Deidentifier
{
    public const string Redacted = "[redacted]";
    public const string Notice =
        "De-identified: names and cities are removed, dates are reduced to the year, and ages over 89 are shown as 90 or older.";

    /// <summary>Returns a de-identified copy of a JSON tool response. The input is not changed.</summary>
    public static string Apply(string json)
    {
        var root = JsonNode.Parse(json);
        if (root is null)
        {
            return json;
        }

        Walk(root);

        if (root is JsonObject obj)
        {
            obj["privacy"] = Notice;
        }

        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static void Walk(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (IsPatient(obj))
                {
                    RedactPatient(obj);
                }
                foreach (var (key, value) in obj.ToList())
                {
                    if (value is JsonValue v && v.TryGetValue<string>(out var s) && FullDate().IsMatch(s))
                    {
                        obj[key] = s[..4];
                    }
                    else if (value is not null)
                    {
                        Walk(value);
                    }
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var item = array[i];
                    if (item is JsonValue v && v.TryGetValue<string>(out var s) && FullDate().IsMatch(s))
                    {
                        array[i] = s[..4];
                    }
                    else if (item is not null)
                    {
                        Walk(item);
                    }
                }
                break;
        }
    }

    /// <summary>A patient summary is the only object carrying both otherNames and a gender or birth date.</summary>
    private static bool IsPatient(JsonObject obj) =>
        obj.ContainsKey("otherNames") && (obj.ContainsKey("gender") || obj.ContainsKey("birthDate") || obj.ContainsKey("deceased"));

    private static void RedactPatient(JsonObject patient)
    {
        if (patient.ContainsKey("name"))
        {
            patient["name"] = Redacted;
        }

        patient["otherNames"] = new JsonArray();
        patient.Remove("city");

        // Safe Harbor groups everyone over 89 together, so their birth year goes too.
        if (patient["age"] is JsonValue ageValue && ageValue.TryGetValue<int>(out var age) && age > 89)
        {
            patient["age"] = "90 or older";
            patient.Remove("birthDate");
        }
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex FullDate();
}
