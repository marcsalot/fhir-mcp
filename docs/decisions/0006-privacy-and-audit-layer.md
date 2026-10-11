# 0006: Enforce privacy and audit in the request pipeline

**Status:** Accepted, October 2026

## Context

Connecting a model to patient data raises the questions a security or compliance team asks first: who accessed which records, can we prove the access log was not altered, can the model work without seeing identities, and can we limit what it can reach? HIPAA's "minimum necessary" standard expects access to be limited to what a purpose requires, and audit controls are a standard safeguard. Most MCP examples answer none of these.

## Options

1. Leave it to the client application and the FHIR server's own controls.
2. Add checks inside each tool.
3. Enforce a policy once, in the MCP request pipeline, so every tool is covered, including tools added later.

## Decision

Option 3, with three controls set by environment variables and read once at startup.

**Audit log** (`FHIR_MCP_AUDIT_LOG`, on by default at `~/.fhir-mcp/audit.jsonl`). A call-tool filter writes one JSON line per call: time, tool, outcome (ok, error or denied), duration, arguments, the patient ids requested or returned, item count, privacy mode and client name. Names and birth dates typed into a search are written as `[redacted]`: the log records who was accessed by id, so it does not become a second copy of identifying data. Each line carries the SHA-256 hash of the line before it, and `audit-verify` checks the chain.

**De-identified mode** (`FHIR_MCP_PRIVACY=deidentified`). Responses pass through `Deidentifier` before reaching the model, following the HIPAA Safe Harbor approach for the fields this server returns: patient names and other names are replaced, cities are removed (the state is kept), every full date is reduced to its year, and ages over 89 become "90 or older" with the birth year removed. Responses say they were de-identified, so the model can explain gaps.

**Tool allowlist** (`FHIR_MCP_TOOLS`). A list-tools filter hides tools that are not enabled, and the call-tool filter refuses them if called anyway, recording the refusal.

Invalid settings stop the server at startup rather than falling back to a looser policy.

## Consequences

- Every call is accounted for, including failures and refusals, with no change needed in individual tools.
- Limits of the hash chain: it detects edits, deletions and reordering anywhere before the last line, but not a change to the last line or lines removed from the end, since nothing follows them. A production system would also copy the latest hash to separate storage (a SIEM, a write-once bucket) at intervals, and would write the log somewhere the server account cannot rewrite.
- Limits of de-identified mode: patient ids are kept so the model can call the other tools, and they link back to a person on the server, so this limits what the model sees rather than producing an anonymous data set. Words a user types (such as a name in a search) still reach the model from the user's side. Free-text fields from the record are passed through; synthetic data has none that identify a person, but real records can, and would need review before this mode is relied on.
- Year-level dates make trends coarser (an A1c series shows years, not months). That is the trade Safe Harbor makes.
- This is not a complete HIPAA program. Access control (who may start the server, SMART on FHIR authorization), encryption, retention and breach procedures sit outside this project.
