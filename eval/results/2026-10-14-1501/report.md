# Evaluation report

Run 2026-10-14 15:01 UTC with `run --models medgemma1.5:4b --modes answer --privacy full --limit 5`.
5 questions about 1 synthetic patients; 5 are medication or allergy questions, which count three times.

Models:

- `medgemma1.5:4b`: Ollama on this machine (4.3B Q4_K_M, digest 433252621ab1), temperature 0, text tool protocol in the fetch mode

## Scoreboard

Score weights each question by importance (medication and allergy questions count three times). A critical miss is a wrong medication or allergy answer. Invented medications counts answers that named a drug not in the patient's record.

| Model / mode / privacy | Score | Passed | Critical misses | Invented medications | Errors | Median time | Tokens in / out |
| --- | --- | --- | --- | --- | --- | --- | --- |
| medgemma1.5:4b / answer / full | 80.0% | 4/5 | 1 | 0 | 0 | 1.6s | 29,649 / 729 |

## By category

| Category | medgemma1.5:4b / answer / full |
| --- | --- |
| allergies | 1/2 |
| medications | 3/3 |

De-identified runs reduce every date to its year, so questions that ask for a full date are expected to fail there. That is the measured cost of the privacy mode, not a model error.

## Failures

Every failed answer, for a person to review. Fixed rules can mark a correct answer wrong when it uses unexpected words; if so, widen the question's patterns rather than trusting the score.

### reyna-drug-allergy (critical)

> Does Reyna Ziemann have any medication (drug) allergies on file?

- **medgemma1.5:4b / answer / full**: expected the answer to start with "no" but it starts with "yes"
  - Answer: "Yes, Reyna Ziemann has allergies to: * Allergy to bee venom * Shellfish allergy * Latex allergy * House dust mite allergy * Allergy to tree pollen * Allergy to peanuts * Allergy to grass pollen * Allergy to mould * Allergy to dairy product * Dander (animal) allergy"

