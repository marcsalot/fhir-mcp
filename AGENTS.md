# Project context for coding agents

This file is the handoff for any AI coding agent (Warp, Claude Code, others) working in this repo. Read it before making changes. The README covers what the project does for a human reader; this file covers how it was built, what was verified, and what comes next.

**First:** if `NOTES.local.md` exists in this folder, read it too. It holds the owner's private context (goals, preferences, where the last session left off) and is kept out of git.

## What this is

A read-only MCP server in C# (.NET 10) that lets Claude answer questions about patient records over FHIR R4. A portfolio project demonstrating FHIR integration and AI tooling. Public synthetic data only. Never add real patient data, credentials, or code from any employer.

## Status

The code is complete and tested: 7 tools, 96 passing tests, every tool verified live through the MCP protocol. It builds and runs on the owner's Mac (.NET 10), and MCP Inspector has been verified there. Remaining work is the Claude Desktop hookup, demo, and publishing (see What's next).

## Layout

- `src/FhirMcp.Core`: FHIR client, name and test-name matching, mappers to compact records. No package dependencies (HttpClient and System.Text.Json only). Must not reference MCP.
- `src/FhirMcp.Server`: MCP host over stdio (ModelContextProtocol 2.2.0) plus a `search` command-line mode for quick checks. Tools live in `Tools/`; every tool returns JSON text built by `ToolSerializer` (camelCase, nulls omitted, non-ASCII left readable).
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
- **Security:** patient ids are validated against the FHIR id pattern before going into a URL; paging only follows `next` links on the configured server; stdout carries the protocol, so all logs go to stderr.

## Verified live (October 2026)

- SMART Health IT R4 sandbox (`https://r4.smarthealthit.org/`, the default) and HAPI public R4 (`https://hapi.fhir.org/baseR4/`).
- Every tool called through the real MCP protocol (initialize, tools/list, tools/call), including error cases.
- **Main demo patient (SMART): Reyna Ziemann `76d2fa2d-66a6-4823-8dab-52cd70822844`.** 52, hypertension, prediabetes, obesity, chronic sinusitis, allergic rhinitis, history of cardiac arrest; 5 active medications (hydrochlorothiazide, ferrous sulfate, fluticasone and albuterol inhalers, diphenhydramine); 10 allergies; 25 lab tests; A1c 2014 to 2020 (peak 6.37% in 2016, 5.96% in 2020); 23 visits, last 2020-05-27.
- Other SMART patients: Mathilda O'Keefe `c7792da2-a72f-4c0a-9dd8-05c5809008f1` (asthma, eczema, epinephrine auto-injector; apostrophe cases); Ima Reinger `6579753c-cb57-4962-a417-a0478b1115aa` (migraine, cardiac arrest history); Jill Tillman `f341c771-5aff-4858-9406-9d45e8a56246` (B12 injection, six allergies); Adolph Smith `d67a0cff-8f7e-46f7-bb99-577a6623e59a` (full-name search example, no active meds); Melony Williamson (maiden name Hammes).
- On HAPI: Fatima Al-Sayed (hyphen cases); patient `ce1b4ef9-474f-45b8-a174-01b9f8e9d614` has a referenced Medication. HAPI is publicly writable and cleared from time to time, so these may disappear; if they do, search for new examples rather than assuming a bug. SMART data is stable.
- Synthetic data runs to about 2021; ages are computed from today, so visits look a few years old.

## How to run

Requires the .NET 10 SDK (on Apple Silicon Macs, the Arm64 installer).

```bash
dotnet build
dotnet test                       # expect 96 passing
dotnet run --project src/FhirMcp.Server -- search --name "reyna ziemann"
FHIR_BASE_URL=https://hapi.fhir.org/baseR4/ dotnet run --project src/FhirMcp.Server -- search --name sayed
npx @modelcontextprotocol/inspector ./src/FhirMcp.Server/bin/Debug/net10.0/FhirMcp.Server
```

Running the server with no arguments looks like it hangs: it is waiting for an MCP client on stdin. Ctrl+C stops it.

For Claude Desktop (Settings > Developer > Edit Config), point `command` at the built executable's full path rather than `dotnet run`; desktop apps on macOS do not always share the terminal's PATH. Fully quit (Cmd+Q) and reopen after editing. The README has the exact JSON and a manual test table.

## What's next

1. Done: set up on the Mac (.NET 10), build, tests passing, MCP Inspector verified.
2. Run the full manual test table in the README with MCP Inspector, using Reyna Ziemann.
3. Connect to Claude Desktop and ask real questions about Reyna Ziemann ("Summarize her health", "Do her medications line up with her conditions?", "Is her blood sugar getting better or worse?").
4. Record a short demo GIF of one question being answered and add it to the README.
5. Push to GitHub and pin the repo.
6. Write a short LinkedIn post about what was built and the FHIR search findings.
7. Stretch: SMART on FHIR (OAuth 2.0), remote HTTP hosting on Azure Container Apps, an MCP prompt that assembles a visit summary.

## Conventions

- Tests for every behavior change; tests must not touch the network. Run `dotnet test` before every commit.
- Keep `FhirMcp.Core` free of MCP and package dependencies; tools in `FhirMcp.Server` stay thin.
- Tool descriptions are the model's only manual: say what matches, what does not, and when to use each option.
- Plain, professional wording in docs and tool descriptions. No em dashes or en dashes in prose.
- Commit messages: a short summary line, then a short paragraph on what and why.
