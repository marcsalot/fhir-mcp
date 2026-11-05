# Evaluation report

Run 2026-10-14 15:27 UTC with `run --models claude --modes answer --privacy full,deidentified`.
50 questions about 5 synthetic patients; 23 are medication or allergy questions, which count three times.

Models:

- `claude-sonnet-5-5`: Anthropic API, temperature 0, native tool use in the fetch mode

## Scoreboard

Score weights each question by importance (medication and allergy questions count three times). A critical miss is a wrong medication or allergy answer. Invented medications counts answers that named a drug not in the patient's record.

| Model / mode / privacy | Score | Passed | Critical misses | Invented medications | Errors | Median time | Tokens in / out |
| --- | --- | --- | --- | --- | --- | --- | --- |
| claude-sonnet-5-5 / answer / full | 0.0% | 0/50 | 23 | 0 | 50 | 0.1s | 0 / 0 |
| claude-sonnet-5-5 / answer / deidentified | 0.0% | 0/50 | 23 | 0 | 50 | 0.1s | 0 / 0 |

## By category

| Category | claude-sonnet-5-5 / answer / full | claude-sonnet-5-5 / answer / deidentified |
| --- | --- | --- |
| absent-data | 0/1 | 0/1 |
| allergies | 0/9 | 0/9 |
| conditions | 0/9 | 0/9 |
| history | 0/4 | 0/4 |
| labs | 0/4 | 0/4 |
| medications | 0/15 | 0/15 |
| visits | 0/3 | 0/3 |
| vitals | 0/5 | 0/5 |

De-identified runs reduce every date to its year, so questions that ask for a full date are expected to fail there. That is the measured cost of the privacy mode, not a model error.

## Failures

Every failed answer, for a person to review. Fixed rules can mark a correct answer wrong when it uses unexpected words; if so, widen the question's patterns rather than trusting the score.

### reyna-active-meds (critical)

> What medications is Reyna Ziemann currently taking?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-bp-med (critical)

> Is Reyna Ziemann on any medication for high blood pressure? If so, which one?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-metformin (critical)

> Is Reyna Ziemann taking metformin?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-drug-allergy (critical)

> Does Reyna Ziemann have any medication (drug) allergies on file?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-allergy-count (critical)

> How many allergies does Reyna Ziemann have on file?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-food-allergies (critical)

> Which food allergies does Reyna Ziemann have?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-diabetes

> Does Reyna Ziemann have diabetes?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-hypertension-onset

> In what year was Reyna Ziemann diagnosed with hypertension?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-a1c-latest

> What was Reyna Ziemann's most recent hemoglobin A1c value?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-a1c-peak

> What was Reyna Ziemann's highest hemoglobin A1c on record, and in what year was it measured?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-a1c-trend

> Has Reyna Ziemann's hemoglobin A1c gone up or down since its peak?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-last-bp

> What was Reyna Ziemann's most recent blood pressure reading?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-weight

> What is Reyna Ziemann's most recent recorded weight?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-last-visit

> What was the date of Reyna Ziemann's most recent visit, and what was it for?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### reyna-sinusitis

> Which of Reyna Ziemann's sinus conditions is still active?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### mathilda-epinephrine (critical)

> Does Mathilda O'Keefe have an epinephrine auto-injector prescribed?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### mathilda-active-meds (critical)

> List Mathilda O'Keefe's active medications.

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### mathilda-acetaminophen (critical)

> Is Mathilda O'Keefe currently taking acetaminophen?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### mathilda-allergies (critical)

> What is Mathilda O'Keefe allergic to?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### mathilda-a1c

> What was Mathilda O'Keefe's most recent hemoglobin A1c?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### mathilda-asthma

> Does Mathilda O'Keefe have asthma?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### mathilda-last-bp

> What was Mathilda O'Keefe's most recent blood pressure?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### mathilda-neck-injury

> Has Mathilda O'Keefe ever had a neck injury? If so, what and when?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### mathilda-dermatitis-onset

> On what date did Mathilda O'Keefe's atopic dermatitis begin?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### ima-migraine

> What kind of migraine does Ima Reinger have, and in what year did it begin?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### ima-active-meds (critical)

> What is Ima Reinger currently prescribed?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### ima-opioid-now (critical)

> Is Ima Reinger currently taking any opioid?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### ima-opioid-ever (critical)

> Has Ima Reinger ever been prescribed an opioid? If so, which?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### ima-pregnancy

> Has Ima Reinger been pregnant? If so, in what year?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### ima-uti-meds

> Which medications were ordered around the time of Ima Reinger's urinary tract infection?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### ima-shellfish (critical)

> Is Ima Reinger allergic to shellfish?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### ima-peanut (critical)

> Is Ima Reinger allergic to peanuts?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### ima-temperature

> What was Ima Reinger's most recent recorded body temperature?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### jill-b12 (critical)

> Is Jill Tillman receiving vitamin B12? If so, in what form?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### jill-nut-allergy (critical)

> Does Jill Tillman have any nut allergies?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### jill-soy (critical)

> Is Jill Tillman allergic to soy?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### jill-bmi

> What is Jill Tillman's most recent BMI, and is it in the obese range?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### jill-active-meds (critical)

> List Jill Tillman's current medications.

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### jill-sinusitis-onset

> Since what year has Jill Tillman had chronic sinusitis?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### jill-last-visit

> When was Jill Tillman's most recent visit, and what was it for?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### jill-diabetes

> Does Jill Tillman have diabetes or prediabetes?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### jill-uti

> In what year did Jill Tillman have a urinary tract infection?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### adolph-active-meds (critical)

> What medications is Adolph Smith currently taking?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### adolph-seizure-med (critical)

> Is Adolph Smith on any medication for his seizure disorder?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### adolph-allergies (critical)

> Does Adolph Smith have any allergies on file?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### adolph-past-meds (critical)

> What medications has Adolph Smith been prescribed in the past?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### adolph-a1c

> What was Adolph Smith's most recent hemoglobin A1c value?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### adolph-anemia

> Does Adolph Smith have anemia?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### adolph-fracture

> Has Adolph Smith ever had a fracture? If so, where and in what year?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

### adolph-last-visit

> What was the date of Adolph Smith's most recent visit?

- **claude-sonnet-5-5 / answer / deidentified**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.
- **claude-sonnet-5-5 / answer / full**: no answer: Anthropic API returned 400: This API key is not scoped to a workspace, so this request must include the anthropic-workspace-id header with the ID of the workspace to use. Add the header, or use an API key that is scoped to a workspace.

