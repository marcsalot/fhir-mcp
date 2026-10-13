using System.Text.RegularExpressions;

namespace FhirMcp.Eval;

/// <summary>
/// Drug names the grader watches for, to catch answers that invent medications. Each entry is
/// one drug with its common names (generic, brand, abbreviation). A drug counts as invented
/// when the answer names it but neither the patient's medication list nor the question does.
/// The list covers the drugs in the sandbox records plus common ones a model might wrongly
/// assume, such as metformin for prediabetes or levetiracetam for seizures.
/// </summary>
public static class DrugVocabulary
{
    public static readonly IReadOnlyList<string[]> Drugs =
    [
        // In the sandbox records used by the questions.
        ["hydrochlorothiazide", "hctz"],
        ["ferrous sulfate", "iron supplement"],
        ["fluticasone", "flovent", "flonase"],
        ["albuterol", "salbutamol", "ventolin", "proair"],
        ["diphenhydramine", "benadryl"],
        ["epinephrine", "epipen", "adrenaline"],
        ["fexofenadine", "allegra"],
        ["nuvaring", "etonogestrel"],
        ["ethinyl estradiol"],
        ["acetaminophen", "paracetamol", "tylenol"],
        ["oxycodone", "percocet"],
        ["terfenadine"],
        ["trinessa", "norgestimate"],
        ["yaz", "drospirenone"],
        ["natazia", "dienogest"],
        ["seasonique", "levonorgestrel"],
        ["mirena"],
        ["jolivette", "norethindrone"],
        ["nitrofurantoin", "macrobid"],
        ["phenazopyridine", "pyridium"],
        ["vitamin b ?12", "cyanocobalamin", "cobalamin"],
        ["ibuprofen", "advil", "motrin"],
        ["naproxen", "aleve"],
        // Common drugs a model might assume without evidence.
        ["metformin", "glucophage"],
        ["insulin"],
        ["lisinopril"],
        ["losartan"],
        ["amlodipine"],
        ["metoprolol"],
        ["atenolol"],
        ["furosemide", "lasix"],
        ["atorvastatin", "lipitor"],
        ["simvastatin"],
        ["rosuvastatin", "crestor"],
        ["aspirin"],
        ["warfarin", "coumadin"],
        ["apixaban", "eliquis"],
        ["clopidogrel", "plavix"],
        ["levothyroxine", "synthroid"],
        ["omeprazole", "prilosec"],
        ["pantoprazole"],
        ["prednisone"],
        ["montelukast", "singulair"],
        ["budesonide", "symbicort", "pulmicort"],
        ["cetirizine", "zyrtec"],
        ["loratadine", "claritin"],
        ["levetiracetam", "keppra"],
        ["phenytoin", "dilantin"],
        ["carbamazepine", "tegretol"],
        ["valproate", "valproic acid", "depakote"],
        ["lamotrigine", "lamictal"],
        ["topiramate", "topamax"],
        ["sumatriptan", "imitrex"],
        ["amoxicillin"],
        ["azithromycin"],
        ["ciprofloxacin", "cipro"],
        ["sertraline", "zoloft"],
        ["gabapentin"],
        ["hydrocodone"],
        ["morphine"],
        ["tramadol"],
    ];

    /// <summary>
    /// Drugs named in the answer that are in neither the patient's medications nor the question.
    /// A drug named only in a negative sentence ("she is not on metformin") is not counted.
    /// </summary>
    public static IReadOnlyList<string> Invented(string normalizedAnswer, string normalizedQuestion, string patientMedicationText)
    {
        var sentences = Regex.Split(normalizedAnswer, @"(?<=[.;!?])\s+|\n+")
            .Where(s => !Negative(s))
            .ToList();

        var invented = new List<string>();
        foreach (var names in Drugs)
        {
            if (!names.Any(n => sentences.Any(s => Mentions(s, n))))
            {
                continue;
            }
            if (names.Any(n => Mentions(patientMedicationText, n) || Mentions(normalizedQuestion, n)))
            {
                continue;
            }
            invented.Add(names[0].Replace(" ?", " "));
        }
        return invented;
    }

    private static bool Negative(string sentence) =>
        Regex.IsMatch(sentence, @"\b(not|no|isn't|aren't|wasn't|doesn't|don't|never|none|without|nor)\b");

    private static bool Mentions(string text, string name) =>
        Regex.IsMatch(text, $@"\b{name}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
