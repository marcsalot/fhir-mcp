using System.Globalization;
using System.Text.Json;
using FhirMcp.Core.Fhir;
using FhirMcp.Core.Models;
using static FhirMcp.Core.Fhir.FhirJson;

namespace FhirMcp.Core.Mapping;

/// <summary>
/// Flattens clinical FHIR resources into compact summaries. Each method keeps the few
/// fields a person would look at and turns codes and structures into readable text.
/// </summary>
public static class ClinicalMapper
{
    public const string RxNorm = "http://www.nlm.nih.gov/research/umls/rxnorm";
    public const string Snomed = "http://snomed.info/sct";
    public const string Loinc = "http://loinc.org";

    /// <param name="medications">
    /// Medication resources returned alongside the orders (via _include), keyed by
    /// "Medication/{id}", used when an order points at a Medication instead of naming the drug.
    /// </param>
    public static MedicationSummary MapMedication(JsonElement request, IReadOnlyDictionary<string, JsonElement> medications)
    {
        var (name, rxNorm) = ResolveMedication(request, medications);

        return new MedicationSummary(
            Name: name,
            Status: GetString(request, "status"),
            Ordered: ParseDate(GetString(request, "authoredOn")),
            Dosage: DescribeDosage(GetArray(request, "dosageInstruction").FirstOrDefault()),
            Reason: Reasons(request).FirstOrDefault(),
            RxNormCode: rxNorm);
    }

    public static ConditionSummary MapCondition(JsonElement condition)
    {
        var onset = GetString(condition, "onsetDateTime")
            ?? (GetObject(condition, "onsetPeriod") is { } p ? GetString(p, "start") : null)
            ?? GetString(condition, "recordedDate");

        return new ConditionSummary(
            Name: ConceptText(GetObject(condition, "code")) ?? "(unnamed condition)",
            ClinicalStatus: ConceptCode(GetObject(condition, "clinicalStatus")),
            VerificationStatus: ConceptCode(GetObject(condition, "verificationStatus")),
            Onset: ParseDate(onset),
            Resolved: ParseDate(GetString(condition, "abatementDateTime")),
            SnomedCode: ConceptCode(GetObject(condition, "code"), Snomed));
    }

    public static EncounterSummary MapEncounter(JsonElement encounter)
    {
        var period = GetObject(encounter, "period");
        var hospitalization = GetObject(encounter, "hospitalization");

        return new EncounterSummary(
            Start: period is { } ps ? ParseDate(GetString(ps, "start")) : null,
            End: period is { } pe ? ParseDate(GetString(pe, "end")) : null,
            Setting: DescribeSetting(GetObject(encounter, "class")),
            Type: GetArray(encounter, "type").Select(t => ConceptText(t)).FirstOrDefault(t => t is not null),
            Reasons: Reasons(encounter).ToList(),
            Status: GetString(encounter, "status"),
            DischargedTo: hospitalization is { } h ? ConceptText(GetObject(h, "dischargeDisposition")) : null);
    }

    public static ObservationSummary MapObservation(JsonElement observation)
    {
        var date = GetString(observation, "effectiveDateTime")
            ?? (GetObject(observation, "effectivePeriod") is { } p ? GetString(p, "start") : null)
            ?? GetString(observation, "issued");

        return new ObservationSummary(
            Name: ConceptText(GetObject(observation, "code")) ?? "(unnamed observation)",
            Date: ParseDate(date),
            Value: DescribeValue(observation),
            Interpretation: GetArray(observation, "interpretation").Select(i => ConceptText(i)).FirstOrDefault(i => i is not null),
            ReferenceRange: DescribeRange(GetArray(observation, "referenceRange").FirstOrDefault()),
            Category: GetArray(observation, "category").Select(c => ConceptCode(c)).FirstOrDefault(c => c is not null),
            LoincCode: ConceptCode(GetObject(observation, "code"), Loinc));
    }

    public static AllergySummary MapAllergy(JsonElement allergy)
    {
        var reactions = GetArray(allergy, "reaction")
            .SelectMany(r => GetArray(r, "manifestation"))
            .Select(m => ConceptText(m))
            .Where(m => m is not null)
            .Select(m => m!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AllergySummary(
            Substance: ConceptText(GetObject(allergy, "code")) ?? "(unnamed substance)",
            ClinicalStatus: ConceptCode(GetObject(allergy, "clinicalStatus")),
            Criticality: GetString(allergy, "criticality"),
            Reactions: reactions,
            Recorded: ParseDate(GetString(allergy, "recordedDate")));
    }

    /// <summary>
    /// A medication order names its drug either inline (medicationCodeableConcept) or by pointing
    /// at a separate Medication resource (medicationReference). Resolve both to a readable name.
    /// </summary>
    private static (string Name, string? RxNorm) ResolveMedication(JsonElement request, IReadOnlyDictionary<string, JsonElement> medications)
    {
        if (GetObject(request, "medicationCodeableConcept") is { } inline)
        {
            return (ConceptText(inline) ?? "(unnamed medication)", ConceptCode(inline, RxNorm));
        }

        if (GetObject(request, "medicationReference") is { } reference)
        {
            var key = NormalizeReference(GetString(reference, "reference"));
            if (key is not null && medications.TryGetValue(key, out var medication)
                && ConceptText(GetObject(medication, "code")) is { } fromResource)
            {
                return (fromResource, ConceptCode(GetObject(medication, "code"), RxNorm));
            }

            if (GetString(reference, "display") is { } display && !string.IsNullOrWhiteSpace(display))
            {
                return (display.Trim(), null);
            }
        }

        return ("(medication not identified)", null);
    }

    /// <summary>"Medication/123", "https://server/fhir/Medication/123" and "Medication/123/_history/2" all become "Medication/123".</summary>
    internal static string? NormalizeReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var parts = reference.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var history = Array.IndexOf(parts, "_history");
        if (history >= 0)
        {
            parts = parts[..history];
        }

        return parts.Length >= 2 ? $"{parts[^2]}/{parts[^1]}" : null;
    }

    private static string? DescribeDosage(JsonElement dosage)
    {
        if (dosage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var text = GetString(dosage, "text");
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        var parts = new List<string>();

        var dose = GetArray(dosage, "doseAndRate").Select(d => GetObject(d, "doseQuantity")).FirstOrDefault(d => d is not null);
        if (dose is { } q && DescribeQuantity(q) is { } doseText)
        {
            // A bare number ("1") reads oddly on its own; label it.
            parts.Add(double.TryParse(doseText, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? $"dose {doseText}" : doseText);
        }

        if (GetObject(dosage, "timing") is { } timing && GetObject(timing, "repeat") is { } repeat)
        {
            var frequency = repeat.TryGetProperty("frequency", out var f) && f.TryGetInt32(out var fi) ? fi : (int?)null;
            var period = repeat.TryGetProperty("period", out var pr) && pr.TryGetDouble(out var pd) ? pd : (double?)null;
            var unit = DescribePeriodUnit(GetString(repeat, "periodUnit"));
            if (frequency is { } fr && period is { } pe && unit is not null)
            {
                var times = fr == 1 ? "once" : fr == 2 ? "twice" : $"{fr} times";
                parts.Add(pe == 1 ? $"{times} a {unit}" : $"{times} every {FormatNumber(pe)} {unit}s");
            }
        }

        if (dosage.TryGetProperty("asNeededBoolean", out var asNeeded) && asNeeded.ValueKind == JsonValueKind.True)
        {
            parts.Add("as needed");
        }
        else if (GetObject(dosage, "asNeededCodeableConcept") is { } forWhat && ConceptText(forWhat) is { } why)
        {
            parts.Add($"as needed for {why}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static string? DescribePeriodUnit(string? unit) => unit switch
    {
        "s" => "second",
        "min" => "minute",
        "h" => "hour",
        "d" => "day",
        "wk" => "week",
        "mo" => "month",
        "a" => "year",
        _ => null,
    };

    /// <summary>Encounter class codes (HL7 v3 ActCode) as plain words.</summary>
    private static string? DescribeSetting(JsonElement? encounterClass)
    {
        if (encounterClass is not { } c)
        {
            return null;
        }

        var code = GetString(c, "code");
        return code switch
        {
            "AMB" => "ambulatory",
            "EMER" => "emergency",
            "IMP" => "inpatient",
            "ACUTE" => "inpatient (acute)",
            "NONAC" => "inpatient (non-acute)",
            "OBSENC" => "observation",
            "SS" => "short stay",
            "HH" => "home health",
            "VR" => "virtual",
            "FLD" => "field",
            "PRENC" => "pre-admission",
            _ => GetString(c, "display") ?? code,
        };
    }

    private static string? DescribeValue(JsonElement observation)
    {
        var own = DescribeSingleValue(observation);
        if (own is not null)
        {
            return own;
        }

        var components = GetArray(observation, "component")
            .Select(c => (Name: ConceptText(GetObject(c, "code")), Value: DescribeSingleValue(c)))
            .Where(c => c.Value is not null)
            .Select(c => c.Name is null ? c.Value! : $"{c.Name}: {c.Value}")
            .ToList();

        if (components.Count > 0)
        {
            return string.Join("; ", components);
        }

        return GetObject(observation, "dataAbsentReason") is { } absent
            ? $"not available ({ConceptText(absent) ?? "no reason given"})"
            : null;
    }

    private static string? DescribeSingleValue(JsonElement element)
    {
        if (GetObject(element, "valueQuantity") is { } quantity)
        {
            return DescribeQuantity(quantity);
        }
        if (GetObject(element, "valueCodeableConcept") is { } concept)
        {
            return ConceptText(concept);
        }
        if (GetString(element, "valueString") is { } text)
        {
            return text;
        }
        if (element.TryGetProperty("valueBoolean", out var b) && b.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return b.GetBoolean() ? "yes" : "no";
        }
        if (element.TryGetProperty("valueInteger", out var i) && i.TryGetInt64(out var iv))
        {
            return iv.ToString(CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static string? DescribeQuantity(JsonElement quantity)
    {
        if (!quantity.TryGetProperty("value", out var v) || !v.TryGetDouble(out var value))
        {
            return null;
        }

        var comparator = GetString(quantity, "comparator") ?? "";
        var unit = GetString(quantity, "unit") ?? GetString(quantity, "code");
        var number = comparator + FormatNumber(value);
        return string.IsNullOrWhiteSpace(unit) ? number : $"{number} {unit}";
    }

    private static string? DescribeRange(JsonElement range)
    {
        if (range.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var text = GetString(range, "text");
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        var low = GetObject(range, "low") is { } l ? DescribeQuantity(l) : null;
        var high = GetObject(range, "high") is { } h ? DescribeQuantity(h) : null;
        return (low, high) switch
        {
            (not null, not null) => $"{low} to {high}",
            (not null, null) => $">= {low}",
            (null, not null) => $"<= {high}",
            _ => null,
        };
    }

    private static IEnumerable<string> Reasons(JsonElement resource) =>
        GetArray(resource, "reasonCode").Select(r => ConceptText(r))
            .Concat(GetArray(resource, "reasonReference").Select(r => GetString(r, "display")))
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
