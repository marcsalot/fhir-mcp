# Project context for coding agents

This file is the handoff for any AI coding agent (Warp, Claude Code, others) working in this repo. Read it before making changes. The README covers what the project does for a human reader; this file covers how it was built, what was verified, and what comes next.

**First:** if `NOTES.local.md` exists in this folder, read it too. It holds the owner's private context (goals, preferences, where the last session left off) and is kept out of git.

## What this is

A read-only MCP server in C# (.NET 8) that lets Claude answer questions about patient records over FHIR R4. A portfolio project demonstrating FHIR integration and AI tooling. Public synthetic data only. Never add real patient data, credentials, or code from any employer.

## Status

The code is complete and tested: 7 tools, 80 passing tests, every tool verified live through the MCP protocol. It was built in a cloud session and handed over as a zip. Remaining work is setup, demo, and publishing (see What's next).

## Layout

- `src/FhirMcp.Core`: FHIR client, name and test-name matching, mappers to compact records. No package dependencies (HttpClient and System.Text.Json only). Must not reference MCP.
- `src/FhirMcp.Server`: MCP host over stdio (ModelContextProtocol 2.2.0) plus a `search` command-line mode for quick checks. Tools live in `Tools/`.
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
- **Name search is done on our side.** FHIR `name=` matches only the start of one stored value: "adolph smith" returns nothing, "Sayed" misses "Al-Sayed". The client sends the longest typed word as a prefix (hyphens kept), checks every word against all of the patient's names (official, maiden), ignoring case, accents, hyphens and apostrophes, and falls back to `name:contains` when nothing matches. Known limit: "AlSayed" typed as one word is not found; the tool's note suggests the hyphenated spelling.
- **Test-name search works the same way.** `code:text=` matches only the start of the full test name, so "a1c" and "systolic" miss. On zero results the client scans up to 5 pages of recent observations and matches words in the test name and its component names, and says so in the note.
- **Medication references** are resolved with `_include=MedicationRequest:medication` in the same request (verified on HAPI: returns "Nitroglycerin").
- **Errors reach the model.** Expected failures are rethrown as `McpException` (see `Tools/ToolErrors.cs`), since the SDK hides other exception messages.
- **Security:** patient ids are validated against the FHIR id pattern before going into a URL; paging only follows `next` links on the configured server; stdout carries the protocol, so all logs go to stderr.

## Verified live (October 2026)

- SMART Health IT R4 sandbox (`https://r4.smarthealthit.org/`, the default) and HAPI public R4 (`https://hapi.fhir.org/baseR4/`).
- Every tool called through the real MCP protocol (initialize, tools/list, tools/call), including error cases.
- Useful synthetic patients on SMART: Adolph Smith `d67a0cff-8f7e-46f7-bb99-577a6623e59a` (conditions, labs, vitals, no active meds); `2d526118-fb1d-44c0-9b0b-b8f6d4f2b4fb` (active medications). Melony Williamson has maiden name Hammes. On HAPI: Fatima Al-Sayed (hyphen cases); patient `ce1b4ef9-474f-45b8-a174-01b9f8e9d614` has a referenced Medication.

## How to run

Requires the .NET 8 SDK (on Apple Silicon Macs, the Arm64 installer).

```bash
dotnet build
dotnet test                       # expect 80 passing
dotnet run --project src/FhirMcp.Server -- search --name "adolph smith"
FHIR_BASE_URL=https://hapi.fhir.org/baseR4/ dotnet run --project src/FhirMcp.Server -- search --name sayed
npx @modelcontextprotocol/inspector ./src/FhirMcp.Server/bin/Debug/net8.0/FhirMcp.Server
```

Running the server with no arguments looks like it hangs: it is waiting for an MCP client on stdin. Ctrl+C stops it.

For Claude Desktop (Settings > Developer > Edit Config), point `command` at the built executable's full path rather than `dotnet run`; desktop apps on macOS do not always share the terminal's PATH. Fully quit (Cmd+Q) and reopen after editing. The README has the exact JSON and a manual test table.

## What's next

1. Set up on the Mac: install the .NET 8 SDK, build, and confirm 80 tests pass.
2. Run the manual test table in the README with MCP Inspector.
3. Connect to Claude Desktop and ask real questions ("Find Adolph Smith and summarize his health", "What were his latest labs?", "Show his A1c trend").
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
