# Project context for coding agents

This file is the handoff for any AI coding agent (Warp, Claude Code, others) working in this repo. Read it before making changes. The README covers what the project does for a human reader; this file covers how it was built, what was verified, and what comes next.

**First:** if `NOTES.local.md` exists in this folder, read it too. It holds the owner's private context (goals, preferences, where the last session left off) and is kept out of git.

## What this is

A read-only MCP server in C# (.NET 10) that lets Claude answer questions about patient records over FHIR R4. A side project exploring FHIR integration and AI tooling. Public synthetic data only. Never add real patient data, credentials, or code from any employer.

## Status

The code is complete and tested: 7 tools, a privacy and audit layer, optional SMART on FHIR sign-in, 161 passing tests, every tool verified live through the MCP protocol. It builds and runs on the owner's Mac (.NET 10), and MCP Inspector has been verified there. It is published at github.com/marcsalot/fhir-mcp and connected to Claude Desktop on the owner's Mac. Remaining work is the demo video (see What's next).

## Layout

- `src/FhirMcp.Core`: FHIR client, name and test-name matching, mappers to compact records. No package dependencies (HttpClient and System.Text.Json only). Must not reference MCP.
- `src/FhirMcp.Core/Privacy`: `PrivacyPolicy` (env var settings), `Deidentifier` (Safe Harbor rules on response JSON), `AuditLog` (hash-chained JSONL).
- `src/FhirMcp.Core/Auth`: `SmartSettings` (env vars, read-only scope check), `SmartAuthClient` (discovery, PKCE, code exchange, refresh), `SmartTokens`, `SmartSession` (current tokens, renewal, patient limit) and `BearerTokenHandler`, token stores (`FileTokenStore`; `KeychainTokenStore` = AES-GCM file with its key in the macOS Keychain via `/usr/bin/security`). `Fhir/AccessScope.cs` holds `IAccessScope` and `PatientAccessDeniedException`.
- `src/FhirMcp.Server`: MCP host over stdio (ModelContextProtocol 2.2.0) plus `search`, `servers`, `audit-verify`, `login`, `logout` and `auth-status` commands (`AuthCommands.cs`). `Core/Fhir/FhirServers.cs` holds the server presets: `FHIR_MCP_SERVER` = `smart` | `hapi` | `smart-login` (the last turns sign-in on with a patient scope unless `FHIR_MCP_AUTH` / `FHIR_MCP_SMART_SCOPE` say otherwise); `FHIR_BASE_URL` for any other server; both set is a startup error. `PolicyFilters` enforces the allowlist and writes audit records around every tool call; `ToolOutput` applies the privacy mode to every response. Tools live in `Tools/`; every tool returns JSON text built by `ToolSerializer` (camelCase, nulls omitted, non-ASCII left readable).
- `tests/FhirMcp.Tests`: xUnit, no network. `FakeHandler` answers by URL and records requests; `FixedTime` pins the clock.

## Tools (all read-only)

| Tool | Purpose |
| --- | --- |
| `search_patients` | Find patients by any part of the name and/or birth date; returns ids for the other tools |
| `get_patient_summary` | Demographics, active conditions and medications, allergies, latest vitals, last visit (six searches in parallel) |
| `get_medications` | Orders with status, dosage in plain words, reason, RxNorm |
| `get_conditions` | Problems with status, onset, resolution, SNOMED CT |
| `get_encounters` | Visits with setting, type, reasons, discharge |
| `get_observations` | Labs and vitals by category, test name, LOINC, date; `latestOnly` for newest per test |
| `get_allergies` | Allergies with criticality and reactions |

## Decisions and why

- **Compact output is the point.** Measured on the SMART sandbox: patient summary 117 KB raw to 2.2 KB; latest labs 212 KB to 3.5 KB. Add a field only if a person would look at it.
- **Name search is done on our side.** FHIR `name=` matches only the start of one stored value: "adolph smith" returns nothing, "Sayed" misses "Al-Sayed". The client sends the longest typed word as a prefix (hyphens kept), checks every word against all of the patient's names (official, maiden), ignoring case, accents, hyphens and apostrophes, and falls back to `name:contains` when nothing matches. Recorded names are split at hyphens and apostrophes, so "Al Sayed" and "O Brien" match directly. Known limit: a name typed without its hyphen or apostrophe ("AlSayed", "okeefe") is not found, because the server cannot match the anchor word; the tool description and no-match note say to use the stored spelling. Unit tests use a fake server that returns every candidate, so they test our matching, not the server's; check name cases live.
- **Test-name search works the same way.** `code:text=` matches only the start of the full test name, so "a1c" and "systolic" miss. On zero results the client scans up to 5 pages of recent observations and matches words in the test name and its component names, and says so in the note.
- **Medication references** are resolved with `_include=MedicationRequest:medication` in the same request (verified on HAPI: returns "Nitroglycerin").
- **Tools return JSON text, not typed objects.** With `UseStructuredContent`, the 2.2.0 SDK's schema generator leaked response properties (such as `note`) into tool input schemas, which made MCP Inspector reject calls. Tools now return strings from `ToolSerializer`, and `SerializationTests` pins the output shape. The `search` command parses and indents that text for display.
- **Errors reach the model.** Expected failures are rethrown as `McpException` (see `Tools/ToolErrors.cs`), since the SDK hides other exception messages.
- **Privacy and audit live in the request pipeline, not in tools.** A new tool is covered automatically as long as it serializes through `ToolOutput` and is added to `PolicyFilters.KnownTools`' type list. See `docs/decisions/0006`.
- **SMART sign-in is a terminal command; the server only uses and renews tokens.** The stdio server has no terminal or good moment to open a browser, so `login` does the browser flow (PKCE S256 required, loopback redirect on a random port, `state` checked) and saves tokens; the server's `BearerTokenHandler` refreshes 60 seconds before expiry and retries once after a 401. Only read scopes are accepted. When every granted resource scope is `patient/` and a patient was chosen, `FhirApiClient` refuses other patient ids before sending (audited as `denied`) and adds `_id` to patient searches. Audit records carry `user` (the ID token's fhirUser, not signature-checked; used for labels only). See `docs/decisions/0007`.
- **Decision records:** `docs/decisions/` explains the major choices. Add a new record for any significant change rather than editing an accepted one.
- **Security:** patient ids are validated against the FHIR id pattern before going into a URL; paging only follows `next` links on the configured server; stdout carries the protocol, so all logs go to stderr.

## Verified live (October 2026)

- SMART Health IT R4 sandbox (`https://r4.smarthealthit.org/`, the default) and HAPI public R4 (`https://hapi.fhir.org/baseR4/`).
- Every tool called through the real MCP protocol (initialize, tools/list, tools/call), including error cases.
- **Main demo patient (SMART): Reyna Ziemann `76d2fa2d-66a6-4823-8dab-52cd70822844`.** 52, hypertension, prediabetes, obesity, chronic sinusitis, allergic rhinitis, history of cardiac arrest; 5 active medications (hydrochlorothiazide, ferrous sulfate, fluticasone and albuterol inhalers, diphenhydramine); 10 allergies; 25 lab tests; A1c 2014 to 2020 (peak 6.37% in 2016, 5.96% in 2020); 23 visits, last 2020-05-27.
- Other SMART patients: Mathilda O'Keefe `c7792da2-a72f-4c0a-9dd8-05c5809008f1` (asthma, eczema, epinephrine auto-injector; apostrophe cases); Ima Reinger `6579753c-cb57-4962-a417-a0478b1115aa` (migraine, cardiac arrest history); Jill Tillman `f341c771-5aff-4858-9406-9d45e8a56246` (B12 injection, six allergies); Adolph Smith `d67a0cff-8f7e-46f7-bb99-577a6623e59a` (full-name search example, no active meds); Melony Williamson (maiden name Hammes).
- On HAPI: Fatima Al-Sayed (hyphen cases); patient `ce1b4ef9-474f-45b8-a174-01b9f8e9d614` has a referenced Medication. HAPI is publicly writable and cleared from time to time, so these may disappear; if they do, search for new examples rather than assuming a bug. SMART data is stable.
- SMART sign-in against the SMART launcher (`https://launch.smarthealthit.org/v/r4/sim/<settings>/fhir/`): the interactive flow (practitioner login, patient picker, approval) in a browser, PKCE code exchange, refresh, refresh-and-retry after a rejected token, the patient limit and its `denied` audit record, and the default `user/*.read` scope (finds any patient). The `smart-login` preset's URL encodes provider standalone launch with PKCE required. The launcher accepts requests with no token (it rejects only invalid ones) and does not enforce scopes, so the client-side patient limit is the only enforcement there.
- Synthetic data runs to about 2021; ages are computed from today, so visits look a few years old.

## How to run

Requires the .NET 10 SDK (on Apple Silicon Macs, the Arm64 installer).

```bash
dotnet build
dotnet test                       # expect 161 passing
dotnet run --project src/FhirMcp.Server -- search --name "reyna ziemann"
FHIR_MCP_SERVER=hapi dotnet run --project src/FhirMcp.Server -- search --name sayed
dotnet run --project src/FhirMcp.Server -- servers   # smart (default), hapi, smart-login; FHIR_BASE_URL for anything else
npx @modelcontextprotocol/inspector ./src/FhirMcp.Server/bin/Debug/net10.0/FhirMcp.Server
```

Running the server with no arguments looks like it hangs: it is waiting for an MCP client on stdin. Ctrl+C stops it.

For Claude Desktop (Settings > Developer > Edit Config), point `command` at the built executable's full path rather than `dotnet run`; desktop apps on macOS do not always share the terminal's PATH. Fully quit (Cmd+Q) and reopen after editing. The README has the exact JSON and a manual test table.

## What's next

1. Done: Mac setup (.NET 10), GitHub (CI green, topics, pinned), Claude Desktop connected and verified with Reyna Ziemann, privacy and audit layer, decision records, SMART on FHIR sign-in (0007).
2. Record a short demo video of one question being answered in Claude Desktop and add it to the README.
3. Try de-identified mode in Claude Desktop (add `"FHIR_MCP_PRIVACY": "deidentified"` to the server's env) and confirm answers still make sense with year-only dates.
4. Done: SMART sign-in verified on the Mac (October 11, 2026): login through the browser, the Keychain key created (`security find-generic-password -s fhir-mcp -a token-encryption-key`), tokens saved encrypted in `~/.fhir-mcp/tokens.enc`, and the one-patient limit applied to search. Next: try `smart-login` in Claude Desktop.
5. Stretch: remote HTTP hosting on Azure Container Apps (with MCP's own authorization replacing the terminal sign-in), an MCP prompt that assembles a visit summary. Write a decision record for each.

## Conventions

- Tests for every behavior change; tests must not touch the network. Run `dotnet test` before every commit.
- Keep `FhirMcp.Core` free of MCP and package dependencies; tools in `FhirMcp.Server` stay thin.
- Tool descriptions are the model's only manual: say what matches, what does not, and when to use each option.
- Plain, professional wording in docs and tool descriptions. No em dashes or en dashes in prose.
- Commit messages: a short summary line, then a short paragraph on what and why.
