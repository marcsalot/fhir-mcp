# 0008: Measure answer quality with a fixed question set and rule-based grading

**Status:** Accepted, October 2026

## Context

The server's tests prove that the tools return the right data. They say nothing about whether a model reading that data answers correctly. That gap matters most where the stakes are highest: a missed allergy or an invented medication is worse than a slightly wrong date. It also blocks decisions that come up with any AI feature: is a newer or cheaper model good enough, does a change to tool descriptions help or hurt, what does de-identified mode cost in accuracy, and can a small medical model running locally (Google's MedGemma) replace a frontier model so patient data never leaves the machine?

## Options

1. **Try questions by hand in Claude Desktop.** Quick, but not repeatable and not comparable across models.
2. **Grade answers with another model** ("LLM as judge"). Handles any wording, but the grader is itself a model whose mistakes and drift are hard to see, and every run costs another set of API calls.
3. **A fixed question set with expected answers checked against the record, graded by rules** (required words, forbidden words, numbers within a tolerance, yes or no first), with every failure listed for a person to review.

## Decision

Option 3, in a separate project (`src/FhirMcp.Eval`) so the server carries no evaluation code.

- **Questions.** 50 questions about 5 synthetic patients, in `eval/questions.json`: medications, allergies, conditions, labs, vital signs, visits, history, and data that is not on file. Each has an expected answer checked against the record and a reference answer written from it. A unit test proves every reference answer passes its own grading, so a question cannot be impossible to pass.
- **Weighting.** Medication and allergy questions are marked critical: they count three times and are reported separately as critical misses.
- **Invented medications.** Any answer that names a drug from a watch list (the sandbox's drugs plus common ones a model might assume, such as metformin or levetiracetam) that is in neither the patient's medication history nor the question fails, unless the drug is only named in a negative sentence ("she is not on metformin").
- **Two modes.** *Answer*: the patient's chart, as the server's tools returned it and frozen in `eval/context`, is in the prompt, which isolates reading and reasoning and makes runs reproducible. *Fetch*: the model gets only the question and calls the real server over MCP, which also tests tool use. Both run with privacy `full` and `deidentified`; the de-identified chart is produced by the server's own `Deidentifier`.
- **Models.** Claude through the Anthropic API with native tool use. Local models through Ollama; because small open models are unreliable with native function calling, the fetch mode gives them a plain text protocol (a JSON object to call a tool, `ANSWER:` to finish). Temperature 0 everywhere; Ollama also gets a fixed seed and a context window large enough for a whole chart.
- **Output.** `eval/results/<time>/report.md` (scoreboard, scores by category, every failure with the answer) and `results.jsonl` (every answer, tool call, timing and token count).

## Consequences

- A model, prompt or tool change can be judged by numbers, and the same run compares a frontier model with local ones under both privacy modes.
- Rule-based grading can mark a correct answer wrong when it is worded unexpectedly. The report lists every failure so a person can check it; a grading mistake is fixed by widening the question's patterns, never by ignoring the score.
- 50 questions about 5 synthetic patients show relative strengths and failure patterns. They are not a clinical validation and say nothing about safety on real records, which would need clinician-written questions, many more patients and human review.
- The answer mode measures reading a compact chart; the fetch mode adds tool use. The two protocols differ (native for Claude, text for local models), so fetch scores compare complete systems, not the models alone.
- De-identified runs are expected to fail questions that need a full date. That is the measured cost of Safe Harbor, reported by category.
- Claude runs cost API usage (roughly a few dollars for a full run); local runs cost only time.
