# Evaluation report

Run 2026-10-14 15:31 UTC with `run --models claude --modes answer --privacy full,deidentified`.
50 questions about 5 synthetic patients; 23 are medication or allergy questions, which count three times.

Models:

- `claude-sonnet-5-5`: Anthropic API, temperature 0, native tool use in the fetch mode

## Scoreboard

Score weights each question by importance (medication and allergy questions count three times). A critical miss is a wrong medication or allergy answer. Invented medications counts answers that named a drug not in the patient's record.

| Model / mode / privacy | Score | Passed | Critical misses | Invented medications | Errors | Median time | Tokens in / out |
| --- | --- | --- | --- | --- | --- | --- | --- |
| claude-sonnet-5-5 / answer / full | 96.9% | 49/50 | 1 | 0 | 0 | 1.7s | 350,823 / 5,284 |
| claude-sonnet-5-5 / answer / deidentified | 95.8% | 46/50 | 0 | 0 | 0 | 2.2s | 342,170 / 9,795 |

## By category

| Category | claude-sonnet-5-5 / answer / full | claude-sonnet-5-5 / answer / deidentified |
| --- | --- | --- |
| absent-data | 1/1 | 1/1 |
| allergies | 8/9 | 9/9 |
| conditions | 9/9 | 8/9 |
| history | 4/4 | 4/4 |
| labs | 4/4 | 4/4 |
| medications | 15/15 | 15/15 |
| visits | 3/3 | 0/3 |
| vitals | 5/5 | 5/5 |

De-identified runs reduce every date to its year, so questions that ask for a full date are expected to fail there. That is the measured cost of the privacy mode, not a model error.

## Failures

Every failed answer, for a person to review. Fixed rules can mark a correct answer wrong when it uses unexpected words; if so, widen the question's patterns rather than trusting the score.

### reyna-last-visit

> What was the date of Reyna Ziemann's most recent visit, and what was it for?

- **claude-sonnet-5-5 / answer / deidentified**: missing: "2020-05-27"
  - Answer: "The record only gives dates as years, so the exact date isn't available. The most recent visit was in 2020, an ambulatory 'Asthma follow-up' with asthma as the recorded reason (status: finished). The record's patient name is redacted, so I can't confirm it belongs to Reyna Ziemann."

### mathilda-dermatitis-onset

> On what date did Mathilda O'Keefe's atopic dermatitis begin?

- **claude-sonnet-5-5 / answer / deidentified**: missing: "2008-04-28"
  - Answer: "The record doesn't give an exact date. Atopic dermatitis is recorded with an onset of 2008, since dates are reduced to the year. The record also shows the patient's name as redacted, so I can't confirm that this patient is Mathilda O'Keefe."

### ima-peanut (critical)

> Is Ima Reinger allergic to peanuts?

- **claude-sonnet-5-5 / answer / full**: expected the answer to start with "no" but it starts with "the"
  - Answer: "The record does not say. Her allergy list has seven entries: bee venom, grass pollen, shellfish, animal dander, tree pollen, mould, and house dust mite. Peanuts are not among them, but the record also doesn't state that she has no peanut allergy, so I can't confirm either way."

### jill-last-visit

> When was Jill Tillman's most recent visit, and what was it for?

- **claude-sonnet-5-5 / answer / deidentified**: missing: "2021-01-31"
  - Answer: "The record's most recent encounter is an ambulatory asthma follow-up in 2021, with asthma as the recorded reason. The patient's name is redacted in this record, so I can't confirm it is Jill Tillman. Dates are shown only as years, and the record also lists a 2021 emergency admission for asthma and a..."

### adolph-last-visit

> What was the date of Adolph Smith's most recent visit?

- **claude-sonnet-5-5 / answer / deidentified**: missing: "2021-01-20"
  - Answer: "The record doesn't give an exact date, because dates are reduced to the year. The most recent visit was an ambulatory check-up (finished) in 2021. The patient's name is redacted in this record, so I can't confirm it is Adolph Smith."

