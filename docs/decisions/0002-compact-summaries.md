# 0002: Return compact summaries, not raw FHIR

**Status:** Accepted, October 2026

## Context

FHIR resources are designed for exchange between systems: every code carries its system, version and display; every reference is a URL; every resource carries metadata. Measured on the SMART Health IT sandbox for one synthetic patient, the six searches behind a patient summary return about 117 KB, and one patient's lab history about 212 KB. A model reading that spends most of its context on structure, and answers get worse as context fills.

## Options

1. Pass FHIR through unchanged and let the model sort it out.
2. Strip known noise (metadata, narrative) but keep the FHIR shape.
3. Map each resource to a small record holding only what a person would look at.

## Decision

Option 3. Each tool returns purpose-built records: a condition is its name, status, onset, resolution date and SNOMED code; a lab result is its name, date, a readable value with units, interpretation and LOINC code. Codes are kept so answers can be traced, but as one field each. Values are formatted for reading (`77.93 mg/dL`, not `77.92600836244159`), and every list says when more items exist than were returned.

Result: the patient summary drops from 117 KB to 2.2 KB, and latest labs from 212 KB to 3.5 KB.

## Consequences

- Answers are faster, cheaper and more accurate, and the model can hold several tool results at once.
- Detail is lost by design. A question that needs a field we dropped (a performer, a specimen, a narrative note) cannot be answered until a mapper adds it. Fields are added when a real question needs them, not in case.
- The mappers are the most important code in the project and carry most of the tests.
