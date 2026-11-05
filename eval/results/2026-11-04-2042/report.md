# Evaluation report

Run 2026-11-04 20:42 UTC with `run --models medgemma:27b --modes answer --privacy deidentified`.
50 questions about 5 synthetic patients; 23 are medication or allergy questions, which count three times.

Models:

- `medgemma:27b`: Ollama on this machine (27.4B Q4_K_M, digest 58238ae38f99), temperature 0, text tool protocol in the fetch mode

## Scoreboard

Score weights each question by importance (medication and allergy questions count three times). A critical miss is a wrong medication or allergy answer. Invented medications counts answers that named a drug not in the patient's record.

| Model / mode / privacy | Score | Passed | Critical misses | Invented medications | Errors | Median time | Tokens in / out |
| --- | --- | --- | --- | --- | --- | --- | --- |
| medgemma:27b / answer / deidentified | 74.0% | 37/50 | 6 | 0 | 0 | 2.4s | 248,244 / 1,876 |

## By category

| Category | medgemma:27b / answer / deidentified |
| --- | --- |
| absent-data | 1/1 |
| allergies | 7/9 |
| conditions | 6/9 |
| history | 3/4 |
| labs | 4/4 |
| medications | 11/15 |
| visits | 0/3 |
| vitals | 5/5 |

De-identified runs reduce every date to its year, so questions that ask for a full date are expected to fail there. That is the measured cost of the privacy mode, not a model error.

## Failures

Every failed answer, for a person to review. Fixed rules can mark a correct answer wrong when it uses unexpected words; if so, widen the question's patterns rather than trusting the score.

### reyna-active-meds (critical)

> What medications is Reyna Ziemann currently taking?

- **medgemma:27b / answer / deidentified**: missing: "hydrochlorothiazide" or "hctz"; missing: "ferrous sulfate" or "iron"; missing: "fluticasone"; missing: "albuterol"; missing: "diphenhydramine" or "benadryl"
  - Answer: "I am sorry, but I cannot answer this question. The patient's name is redacted in the record."

### reyna-food-allergies (critical)

> Which food allergies does Reyna Ziemann have?

- **medgemma:27b / answer / deidentified**: missing: "peanut"; missing: "shellfish"; missing: "dairy" or "milk"
  - Answer: "I am sorry, I cannot answer the question because the patient's name is redacted."

### reyna-diabetes

> Does Reyna Ziemann have diabetes?

- **medgemma:27b / answer / deidentified**: missing: "prediabet" or "pre-diabet"
  - Answer: "No."

### reyna-last-visit

> What was the date of Reyna Ziemann's most recent visit, and what was it for?

- **medgemma:27b / answer / deidentified**: missing: "2020-05-27"
  - Answer: "The patient's name is redacted as '[redacted]'. The most recent visit was in 2020 for Asthma follow-up."

### mathilda-active-meds (critical)

> List Mathilda O'Keefe's active medications.

- **medgemma:27b / answer / deidentified**: missing: "nuvaring" or "vaginal ring"; missing: "fluticasone"; missing: "albuterol"; missing: "epinephrine" or "epipen" or "auto-injector"; missing: "fexofenadine" or "allegra"
  - Answer: "I cannot answer the question because the patient's name is redacted."

### mathilda-neck-injury

> Has Mathilda O'Keefe ever had a neck injury? If so, what and when?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"; missing: "whiplash"; missing: "2013"
  - Answer: "No. The patient's name is redacted. The record does not contain information about a neck injury."

### mathilda-dermatitis-onset

> On what date did Mathilda O'Keefe's atopic dermatitis begin?

- **medgemma:27b / answer / deidentified**: missing: "2008-04-28"
  - Answer: "The patient's name is redacted as '[redacted]'. The onset date for Atopic dermatitis is 2008."

### ima-opioid-now (critical)

> Is Ima Reinger currently taking any opioid?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "no" but it starts with "yes"
  - Answer: "Yes. The patient is currently taking Acetaminophen 325 MG / Oxycodone Hydrochloride 5 MG Oral Tablet, which contains an opioid. This medication was ordered in 2016 and is listed as stopped. However, the patient is also currently taking Trinessa 28 Day Pack and NDA020800 0.3 ML Epinephrine 1 MG/ML Au..."

### ima-peanut (critical)

> Is Ima Reinger allergic to peanuts?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "no" but it starts with "the"
  - Answer: "The patient record does not contain information about an allergy to peanuts."

### jill-last-visit

> When was Jill Tillman's most recent visit, and what was it for?

- **medgemma:27b / answer / deidentified**: missing: "2021-01-31"
  - Answer: "The patient's name is redacted. The most recent visit was in 2021 for Asthma follow-up."

### adolph-active-meds (critical)

> What medications is Adolph Smith currently taking?

- **medgemma:27b / answer / deidentified**: missing: "no active" or "no current" or "none" or "not currently" or "isn't currently" or "is not currently" or "not taking" or "no medication"
  - Answer: "The patient's record does not list any current medications. The medications listed in the 'get_medications' resource are all marked as 'stopped'."

### adolph-anemia

> Does Adolph Smith have anemia?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"
  - Answer: "No. The patient's name is redacted, not Adolph Smith. The patient does have anemia, which is listed as an active condition with onset in 1973."

### adolph-last-visit

> What was the date of Adolph Smith's most recent visit?

- **medgemma:27b / answer / deidentified**: missing: "2021-01-20"
  - Answer: "The patient's name is redacted. The most recent encounter start date is 2021."

