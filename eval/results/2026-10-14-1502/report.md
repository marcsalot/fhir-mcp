# Evaluation report

Run 2026-10-14 15:02 UTC with `run --models medgemma1.5:4b --modes answer --privacy full,deidentified`.
50 questions about 5 synthetic patients; 23 are medication or allergy questions, which count three times.

Models:

- `medgemma1.5:4b`: Ollama on this machine (4.3B Q4_K_M, digest 433252621ab1), temperature 0, text tool protocol in the fetch mode

## Scoreboard

Score weights each question by importance (medication and allergy questions count three times). A critical miss is a wrong medication or allergy answer. Invented medications counts answers that named a drug not in the patient's record.

| Model / mode / privacy | Score | Passed | Critical misses | Invented medications | Errors | Median time | Tokens in / out |
| --- | --- | --- | --- | --- | --- | --- | --- |
| medgemma1.5:4b / answer / full | 80.2% | 41/50 | 5 | 0 | 0 | 0.9s | 268,628 / 7,024 |
| medgemma1.5:4b / answer / deidentified | 75.0% | 36/50 | 5 | 0 | 0 | 6.2s | 242,844 / 15,819 |

## By category

| Category | medgemma1.5:4b / answer / full | medgemma1.5:4b / answer / deidentified |
| --- | --- | --- |
| absent-data | 1/1 | 1/1 |
| allergies | 7/9 | 5/9 |
| conditions | 5/9 | 6/9 |
| history | 4/4 | 2/4 |
| labs | 4/4 | 4/4 |
| medications | 12/15 | 14/15 |
| visits | 3/3 | 0/3 |
| vitals | 5/5 | 4/5 |

De-identified runs reduce every date to its year, so questions that ask for a full date are expected to fail there. That is the measured cost of the privacy mode, not a model error.

## Failures

Every failed answer, for a person to review. Fixed rules can mark a correct answer wrong when it uses unexpected words; if so, widen the question's patterns rather than trusting the score.

### reyna-drug-allergy (critical)

> Does Reyna Ziemann have any medication (drug) allergies on file?

- **medgemma1.5:4b / answer / deidentified**: expected the answer to start with "no" but it starts with "yes"
  - Answer: "Yes. Reyna Ziemann has allergies to bee venom, shellfish, latex, house dust mite, tree pollen, peanuts, grass pollen, mould, dairy product, and dander (animal)."
- **medgemma1.5:4b / answer / full**: expected the answer to start with "no" but it starts with "yes"
  - Answer: "Yes, Reyna Ziemann has allergies to: * Allergy to bee venom * Shellfish allergy * Latex allergy * House dust mite allergy * Allergy to tree pollen * Allergy to peanuts * Allergy to grass pollen * Allergy to mould * Allergy to dairy product * Dander (animal) allergy"

### reyna-diabetes

> Does Reyna Ziemann have diabetes?

- **medgemma1.5:4b / answer / deidentified**: missing: "prediabet" or "pre-diabet"
  - Answer: "No. The record does not contain information about Reyna Ziemann having diabetes."
- **medgemma1.5:4b / answer / full**: missing: "prediabet" or "pre-diabet"
  - Answer: "No. The record does not contain information about diabetes."

### reyna-last-visit

> What was the date of Reyna Ziemann's most recent visit, and what was it for?

- **medgemma1.5:4b / answer / deidentified**: missing: "2020-05-27"; missing: "asthma"
  - Answer: "Yes, Reyna Ziemann's most recent visit was on 2020-03-11 for an Encounter for symptom."

### reyna-sinusitis

> Which of Reyna Ziemann's sinus conditions is still active?

- **medgemma1.5:4b / answer / full**: missing: "chronic sinusitis"
  - Answer: "No. The record does not contain information about Reyna Ziemann's sinus conditions."

### mathilda-neck-injury

> Has Mathilda O'Keefe ever had a neck injury? If so, what and when?

- **medgemma1.5:4b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"; missing: "whiplash"; missing: "2013"
  - Answer: "No. The patient's name is redacted, and there is no record of a neck injury for this patient. 'Mathilda O'Keefe' is not mentioned in the record."

### mathilda-dermatitis-onset

> On what date did Mathilda O'Keefe's atopic dermatitis begin?

- **medgemma1.5:4b / answer / deidentified**: missing: "2008-04-28"
  - Answer: "No. The record does not contain the date the patient's atopic dermatitis began. The record states the onset was in 2008."

### ima-migraine

> What kind of migraine does Ima Reinger have, and in what year did it begin?

- **medgemma1.5:4b / answer / full**: missing: "without aura"; missing: "2016"
  - Answer: "No matching observations. Try a broader name, no category, or no date."

### ima-active-meds (critical)

> What is Ima Reinger currently prescribed?

- **medgemma1.5:4b / answer / full**: mentions something not in the record: "mirena"; mentions something not in the record: "yaz"; mentions something not in the record: "seasonique"; mentions something not in the record: "natazia"; mentions something not in the record: "jolivette"; mentions something not in the record: "oxycodone"; mentions something not in the record: "nitrofurantoin"; mentions something not in the record: "phenazopyridine"
  - Answer: "Ima Reinger is currently prescribed: * Trinessa 28 Day Pack * Yaz 28 Day Pack * Natazia 28 Day Pack * Yaz 28 Day Pack * Nitrofurantoin 5 MG/ML Oral Suspension * Phenazopyridine hydrochloride 100 MG Oral Tablet * Etonogestrel 68 MG Drug Implant * Acetaminophen 325 MG / Oxycodone Hydrochloride 5 MG Or..."

### ima-shellfish (critical)

> Is Ima Reinger allergic to shellfish?

- **medgemma1.5:4b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"
  - Answer: "No. The record does not contain information about Ima Reinger's allergies."

### jill-nut-allergy (critical)

> Does Jill Tillman have any nut allergies?

- **medgemma1.5:4b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"; missing: "peanut"
  - Answer: "No. The record does not contain any allergies for Jill Tillman."
- **medgemma1.5:4b / answer / full**: missing: "peanut"
  - Answer: "Yes. Jill Tillman has allergies to nut, tree pollen, bee venom, and soya."

### jill-soy (critical)

> Is Jill Tillman allergic to soy?

- **medgemma1.5:4b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"
  - Answer: "No. The record does not contain information about Jill Tillman's allergies."

### jill-bmi

> What is Jill Tillman's most recent BMI, and is it in the obese range?

- **medgemma1.5:4b / answer / deidentified**: missing: "obes"; missing the value 30.38
  - Answer: "No. The record does not contain information about a patient named Jill Tillman. The patient's name is redacted."

### jill-sinusitis-onset

> Since what year has Jill Tillman had chronic sinusitis?

- **medgemma1.5:4b / answer / full**: missing: "1988"
  - Answer: "No. The record does not contain information about the year Jill Tillman had chronic sinusitis."

### jill-last-visit

> When was Jill Tillman's most recent visit, and what was it for?

- **medgemma1.5:4b / answer / deidentified**: missing: "2021-01-31"
  - Answer: "Yes. The most recent visit was on 2021-12-01 for Asthma follow-up."

### adolph-active-meds (critical)

> What medications is Adolph Smith currently taking?

- **medgemma1.5:4b / answer / deidentified**: missing: "no active" or "no current" or "none" or "not currently" or "isn't currently" or "is not currently" or "not taking" or "no medication"
  - Answer: "Adolph Smith is currently taking: * Naproxen sodium 220 MG Oral Tablet * Ibuprofen 200 MG Oral Tablet * Naproxen sodium 220 MG Oral Tablet"
- **medgemma1.5:4b / answer / full**: missing: "no active" or "no current" or "none" or "not currently" or "isn't currently" or "is not currently" or "not taking" or "no medication"
  - Answer: "Adolph Smith is currently taking: * Naproxen sodium 220 MG Oral Tablet (status: stopped) * Ibuprofen 200 MG Oral Tablet (status: stopped) * Naproxen sodium 220 MG Oral Tablet (status: stopped)"

### adolph-seizure-med (critical)

> Is Adolph Smith on any medication for his seizure disorder?

- **medgemma1.5:4b / answer / full**: expected the answer to start with "no" but it starts with "yes"
  - Answer: "Yes. Adolph Smith is on Naproxen sodium 220 MG Oral Tablet and Ibuprofen 200 MG Oral Tablet for his seizure disorder."

### adolph-anemia

> Does Adolph Smith have anemia?

- **medgemma1.5:4b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"
  - Answer: "No. The record does not contain the name 'Adolph Smith'. However, the record does list 'Anemia (disorder)' as an active condition."

### adolph-fracture

> Has Adolph Smith ever had a fracture? If so, where and in what year?

- **medgemma1.5:4b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"; missing: "wrist"; missing: "2018"
  - Answer: "No. The record does not contain information about fractures for this patient. The patient's name is redacted, but the record does not list any fractures."

### adolph-last-visit

> What was the date of Adolph Smith's most recent visit?

- **medgemma1.5:4b / answer / deidentified**: missing: "2021-01-20"
  - Answer: "Yes. The most recent visit was on 1978-01-01."

