# Architecture decision records

Each record explains one significant choice: the situation, the options considered, what was decided, and what it costs. They are written for a reviewer who wants to know why the code looks the way it does, not only what it does.

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-read-only-by-design.md) | The server is read-only | Accepted |
| [0002](0002-compact-summaries.md) | Return compact summaries, not raw FHIR | Accepted |
| [0003](0003-match-names-on-our-side.md) | Match names and test names on our side | Accepted |
| [0004](0004-dependency-free-core.md) | Keep the core library free of dependencies | Accepted |
| [0005](0005-tools-return-json-text.md) | Tools return JSON text, not typed objects | Accepted |
| [0006](0006-privacy-and-audit-layer.md) | Enforce privacy and audit in the request pipeline | Accepted |
| [0007](0007-smart-on-fhir-sign-in.md) | Sign in with SMART on FHIR, as a user, from the terminal | Accepted |
| [0008](0008-model-evaluation.md) | Measure answer quality with a fixed question set and rule-based grading | Accepted |

Format: Context, Options, Decision, Consequences. Records are not edited after acceptance except to mark them superseded; a changed decision gets a new record.
