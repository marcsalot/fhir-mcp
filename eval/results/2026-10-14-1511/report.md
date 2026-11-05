# Evaluation report

Run 2026-10-14 15:11 UTC with `run --models medgemma:27b --modes answer --privacy full,deidentified`.
50 questions about 5 synthetic patients; 23 are medication or allergy questions, which count three times.

Models:

- `medgemma:27b`: Ollama on this machine (27.4B Q4_K_M, digest 58238ae38f99), temperature 0, text tool protocol in the fetch mode

## Scoreboard

Score weights each question by importance (medication and allergy questions count three times). A critical miss is a wrong medication or allergy answer. Invented medications counts answers that named a drug not in the patient's record.

| Model / mode / privacy | Score | Passed | Critical misses | Invented medications | Errors | Median time | Tokens in / out |
| --- | --- | --- | --- | --- | --- | --- | --- |
| medgemma:27b / answer / full | 91.7% | 46/50 | 2 | 0 | 0 | 2.0s | 268,628 / 2,202 |
| medgemma:27b / answer / deidentified | 64.6% | 32/50 | 8 | 0 | 0 | 2.8s | 242,844 / 2,138 |

## By category

| Category | medgemma:27b / answer / full | medgemma:27b / answer / deidentified |
| --- | --- | --- |
| absent-data | 1/1 | 1/1 |
| allergies | 9/9 | 5/9 |
| conditions | 8/9 | 5/9 |
| history | 4/4 | 1/4 |
| labs | 4/4 | 4/4 |
| medications | 12/15 | 11/15 |
| visits | 3/3 | 0/3 |
| vitals | 5/5 | 5/5 |

De-identified runs reduce every date to its year, so questions that ask for a full date are expected to fail there. That is the measured cost of the privacy mode, not a model error.

## Failures

Every failed answer, for a person to review. Fixed rules can mark a correct answer wrong when it uses unexpected words; if so, widen the question's patterns rather than trusting the score.

### reyna-metformin (critical)

> Is Reyna Ziemann taking metformin?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "no" but it starts with "the"
  - Answer: "The patient's record does not contain information about whether the patient is taking metformin."

### reyna-food-allergies (critical)

> Which food allergies does Reyna Ziemann have?

- **medgemma:27b / answer / deidentified**: missing: "shellfish"
  - Answer: "The patient's record does not contain the name 'Reyna Ziemann'. The record lists the following food allergies: Allergy to dairy product, Allergy to peanuts."

### reyna-diabetes

> Does Reyna Ziemann have diabetes?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "no" but it starts with "the"; missing: "prediabet" or "pre-diabet"
  - Answer: "The patient's name is redacted, not Reyna Ziemann. The record does not contain information about diabetes."
- **medgemma:27b / answer / full**: missing: "prediabet" or "pre-diabet"
  - Answer: "No."

### reyna-last-visit

> What was the date of Reyna Ziemann's most recent visit, and what was it for?

- **medgemma:27b / answer / deidentified**: missing: "2020-05-27"
  - Answer: "The patient's name is redacted, not Reyna Ziemann. The most recent visit was in 2020 for Asthma follow-up."

### mathilda-asthma

> Does Mathilda O'Keefe have asthma?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"
  - Answer: "No. The patient's name is redacted, not Mathilda O'Keefe. However, the record does show that the patient has 'Childhood asthma', which is an active condition."

### mathilda-neck-injury

> Has Mathilda O'Keefe ever had a neck injury? If so, what and when?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"
  - Answer: "No. The patient's name is redacted, not Mathilda O'Keefe. The record does contain information about a past condition: Whiplash injury to neck, which was resolved in 2013."

### mathilda-dermatitis-onset

> On what date did Mathilda O'Keefe's atopic dermatitis begin?

- **medgemma:27b / answer / deidentified**: missing: "2008-04-28"
  - Answer: "The patient's name is redacted, but the record shows a condition named 'Atopic dermatitis' with an onset date of '2008'."

### ima-opioid-now (critical)

> Is Ima Reinger currently taking any opioid?

- **medgemma:27b / answer / full**: expected the answer to start with "no" but it starts with "yes"
  - Answer: "Yes. The patient is currently taking Acetaminophen 325 MG / Oxycodone Hydrochloride 5 MG Oral Tablet, which contains an opioid."

### ima-pregnancy

> Has Ima Reinger been pregnant? If so, in what year?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"
  - Answer: "No. The patient's name is redacted, not Ima Reinger. However, the record does show a 'Normal pregnancy' condition with onset in 2016 and resolution in 2016."

### ima-uti-meds

> Which medications were ordered around the time of Ima Reinger's urinary tract infection?

- **medgemma:27b / answer / full**: missing: "nitrofurantoin"; missing: "phenazopyridine"
  - Answer: "```tool_code from datetime import datetime, timedelta def parse_date(date_string): return datetime.strptime(date_string, '%Y-%m-%d') def get_medications_around_event(patient_id, event_date_str, start_delta_days=30, end_delta_days=30): ''' Retrieves medications ordered within a time window around a s..."

### ima-shellfish (critical)

> Is Ima Reinger allergic to shellfish?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"
  - Answer: "No. The patient's name is redacted, not Ima Reinger. However, the record does state that the patient has a 'Shellfish allergy' with clinical status 'active'."

### jill-nut-allergy (critical)

> Does Jill Tillman have any nut allergies?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"; missing: "peanut"
  - Answer: "No. The patient's name is redacted, not Jill Tillman. However, the record does show that the patient has an allergy to nut, recorded in 1972."

### jill-soy (critical)

> Is Jill Tillman allergic to soy?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"
  - Answer: "No. The patient's name is redacted, not Jill Tillman. The record does state that the patient has an allergy to soya, recorded in 1972."

### jill-active-meds (critical)

> List Jill Tillman's current medications.

- **medgemma:27b / answer / deidentified**: missing: "trinessa"; missing: "b12" or "b 12" or "cobalamin"; missing: "diphenhydramine" or "benadryl"; missing: "fluticasone"; missing: "albuterol"
  - Answer: "I cannot answer the question because the patient's name is redacted."

### jill-last-visit

> When was Jill Tillman's most recent visit, and what was it for?

- **medgemma:27b / answer / deidentified**: missing: "2021-01-31"
  - Answer: "The patient's name is redacted, not Jill Tillman. The most recent visit was in 2021 for Asthma follow-up."

### adolph-active-meds (critical)

> What medications is Adolph Smith currently taking?

- **medgemma:27b / answer / deidentified**: missing: "no active" or "no current" or "none" or "not currently" or "isn't currently" or "is not currently" or "not taking" or "no medication"
  - Answer: "The patient's name is redacted, so I cannot answer the question."
- **medgemma:27b / answer / full**: missing: "no active" or "no current" or "none" or "not currently" or "isn't currently" or "is not currently" or "not taking" or "no medication"
  - Answer: "The record does not contain information about medications Adolph Smith is currently taking. The `get_medications` resource shows medications that were previously ordered, but all have a status of 'stopped'."

### adolph-past-meds (critical)

> What medications has Adolph Smith been prescribed in the past?

- **medgemma:27b / answer / deidentified**: missing: "ibuprofen"; missing: "naproxen"
  - Answer: "The patient's name is redacted, so I cannot answer the question."

### adolph-anemia

> Does Adolph Smith have anemia?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"
  - Answer: "No. The patient's name is redacted, not Adolph Smith. The patient does have anemia, which is listed as an active condition with onset in 1973."

### adolph-fracture

> Has Adolph Smith ever had a fracture? If so, where and in what year?

- **medgemma:27b / answer / deidentified**: expected the answer to start with "yes" but it starts with "no"
  - Answer: "No. The patient's name is redacted, not Adolph Smith. The record does contain information about a fracture. The patient had a 'Fracture subluxation of wrist' in 2018."

### adolph-last-visit

> What was the date of Adolph Smith's most recent visit?

- **medgemma:27b / answer / deidentified**: missing: "2021-01-20"
  - Answer: "The patient's name is redacted, so I cannot answer the question."

