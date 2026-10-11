# 0003: Match names and test names on our side

**Status:** Accepted, October 2026

## Context

Testing against public servers showed that FHIR string search is more literal than people expect. `name=` matches only the start of one stored value, so:

| Typed | Server result |
| --- | --- |
| `Reyna Ziemann` | Nothing; the phrase must match one value |
| `Sayed` | Misses "Al-Sayed"; the stored value starts with "Al-" |
| `ham` | Finds maiden names starting "Ham", with no hint why |

Test names behave the same way through `code:text=`: `a1c` misses "Hemoglobin A1c/Hemoglobin.total in Blood", and `systolic` misses "Blood Pressure", whose systolic reading is a component. This is correct behavior for the standard, but a model turns natural language into search terms, and those terms look like what a person would type.

## Options

1. Pass the model's terms straight to the server and let it retry.
2. Teach the model FHIR search rules in the tool description.
3. Ask the server a narrow question and do the matching here.

## Decision

Option 3, with honest reporting. For names: send the most specific word typed as a prefix, check every typed word against all of each candidate's names (official, maiden, nickname) ignoring case, accents, hyphens and apostrophes, and fall back to `:contains` if nothing matches. For tests: try `code:text`, and if it finds nothing, scan recent results and match words in the test name and its component names. Every response says how results were found when it was not a plain match.

## Consequences

- "Reyna Ziemann", "fatima al sayed" and "a1c" work the way a person expects.
- Matching costs extra work: a multi-word name search fetches up to 100 candidates to check, and the test-name fallback scans up to 1,000 recent results.
- One limit remains: a name typed without its hyphen or apostrophe ("AlSayed", "okeefe") is not found, because the server cannot match the first word. The tool description tells the model to use the stored spelling.
- Unit tests use a fake server that returns every candidate, so they test our matching, not the server's. Name cases are also checked live.
