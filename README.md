# FHIR MCP Server

An MCP server, written in C# on .NET 8, that lets Claude and other MCP clients answer questions about patient records over FHIR R4. Ask "What is Adolph Smith being treated for, and what were his latest labs?" and the server runs the FHIR searches, trims the verbose responses down to what matters, and hands back a compact answer.

> **Synthetic data only.** This project points at public FHIR test servers seeded with synthetic patients. It is read-only and is not connected to any real patient data.

## Status

| Piece | State |
| --- | --- |
| FHIR client and patient search | Done |
| Name matching (full names, hyphens, accents, maiden names, fallback) | Done |
| MCP server with 7 read-only tools | Done |
| Patient summary, medications, conditions, encounters, observations, allergies | Done |
| SMART on FHIR authentication | Stretch goal |

## How it works

```
Claude  -->  MCP server (this project)  -->  FHIR R4 server
                    |
                    +-- matches names the way a person types them
                    +-- flattens Bundles into small summaries
                    +-- caps results and says when more exist
                    +-- turns FHIR errors (OperationOutcome) into plain messages
```

FHIR responses are large, which wastes the model's context and makes answers worse. Most of the work in this project is in shaping requests and responses, not in calling the API. Measured against the SMART Health IT sandbox for one synthetic patient:

| Question | Raw FHIR from the server | What the tool returns |
| --- | --- | --- |
| Patient summary (six searches) | 117 KB | 2.2 KB |
| Latest result of each lab test | 212 KB | 3.5 KB |

## Name search

FHIR's default name search surprised me in testing against public servers:

| Typed | FHIR server alone | This server |
| --- | --- | --- |
| `Adolph Smith` | 0 results (the phrase must match one name value) | Adolph Smith |
| `Sayed` (for Al-Sayed) | 0 results (only the start of "Al-Sayed" matches) | Fatima Al-Sayed, flagged as a looser match |
| `fatima al sayed` | 0 results | Fatima Al-Sayed |
| `Hammes` | Melony Williamson, with no hint why | Melony Williamson, showing "Melony Hammes (maiden)" |

How it works:

1. The server is asked about the most specific word typed (the longest), as a prefix, with hyphens kept.
2. Every word typed is then checked against all of each candidate's names: official, maiden, nickname. Case, accents, hyphens and apostrophes are ignored, so `arisso` finds Arissó and `obrien` finds O'Brien.
3. If nothing matches, the search is retried with FHIR's `:contains` modifier, and the result says so.
4. Each result lists the patient's other names, so it is clear why they matched.

Known limit: a hyphenated name typed as one word (`AlSayed`) is not found, because the server stores the hyphen and offers no way to match across it. The tool's reply suggests the hyphenated spelling.

## Tools

| Tool | What it does |
| --- | --- |
| `search_patients` | Finds patients by any part of the name and/or birth date. Returns the id the other tools take, plus name, other names, gender, birth date, age, city, state, and a deceased flag. |
| `get_patient_summary` | One patient at a glance: demographics, active conditions and medications, allergies, the latest reading of each vital sign, and the last visit. Runs six searches in parallel. |
| `get_medications` | Medication orders, newest first, with status, dosage in plain words ("500 mg, twice a day"), reason, and RxNorm code. Active or all. |
| `get_conditions` | Problems and diagnoses with status, onset and resolution dates, and SNOMED CT code. Active (including recurrences and relapses) or all. |
| `get_encounters` | Visits and stays, newest first, with setting (ambulatory, emergency, inpatient), type, reasons, and discharge disposition. |
| `get_observations` | Labs, vitals, and other observations by category, test name, LOINC code, or date. `latestOnly` keeps the newest result of each test. |
| `get_allergies` | Allergies and intolerances with criticality and reactions. |

Tools are read-only and annotated as such. Errors (a bad date, an unknown patient, an unknown category) come back as messages the model can act on.

### Details that make the answers better

- **Readable values.** Synthetic data stores values like `77.92600836244159`; tools return `77.93 mg/dL`. Blood pressure reads `Systolic Blood Pressure: 124.7 mm[Hg]; Diastolic Blood Pressure: 81.68 mm[Hg]`, and coded answers read as text (`Never smoker`).
- **Test names found the way people say them.** Like names, FHIR's `code:text` search matches only the start of a test's full name, so `a1c` misses "Hemoglobin A1c/Hemoglobin.total in Blood" and `systolic` misses "Blood Pressure". When the server finds nothing, the tool scans the patient's recent results and matches words anywhere in the test name or its parts, and says it did.
- **References resolved in one request.** A medication order may point at a separate Medication resource instead of naming the drug. The tool asks the server to include those (`_include`) and returns the drug's name.
- **Honest notes.** Each list says when more items exist than were returned, and an empty allergy list says "none on file", not "no allergies".

## Getting started

Requires the .NET 8 SDK.

```bash
dotnet build
dotnet test
```

Try a search from the command line (defaults to the SMART Health IT R4 sandbox):

```bash
dotnet run --project src/FhirMcp.Server -- search --name "adolph smith"
dotnet run --project src/FhirMcp.Server -- search --name ham --contains
```

Use a different server by setting `FHIR_BASE_URL`, for example `https://hapi.fhir.org/baseR4/`.

### Connect it to Claude Desktop

Build once, then add the server to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "fhir": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/fhir-mcp/src/FhirMcp.Server", "--no-build"],
      "env": { "FHIR_BASE_URL": "https://r4.smarthealthit.org/" }
    }
  }
}
```

Or with Claude Code:

```bash
claude mcp add fhir -- dotnet run --project /path/to/fhir-mcp/src/FhirMcp.Server --no-build
```

## Project layout

```
src/FhirMcp.Core      FHIR client, name matching, mapping to compact summaries (no dependencies)
src/FhirMcp.Server    MCP server over stdio, plus a "search" command for quick checks
tests/FhirMcp.Tests   xUnit tests with synthetic FHIR fixtures
```

## Design choices

- **No dependencies in the core library.** The FHIR client uses `HttpClient` and `System.Text.Json` and reads only the fields it needs, which keeps the output shaping explicit and easy to test.
- **Read-only, bounded queries.** Every search requires real criteria and caps its results (50 at most), so a tool call can never page through a whole server.
- **Honest data.** Partial birth dates (`1955`) are not turned into exact ages, deceased patients get no current age, and looser matches are labeled as such.
- **Testable without a network.** Tests run against synthetic FHIR responses through a fake HTTP handler, and the clock is injected through `TimeProvider`. 80 tests cover mapping, name and test matching, request shape, limits, and errors.

## Security notes

- Read-only: the client only issues `GET` requests.
- Patient ids are checked against the FHIR id format before they go into a URL, so a malformed or injected value (`../Patient?_count=1000`) is rejected without a request.
- Paging only follows `next` links that point back at the configured server, so a response cannot steer the client to another host.
- Synthetic data only; do not point this at a server holding real patient data without adding authentication, audit logging, and a review of what is sent to the model.
- Error messages returned to the model contain the server's diagnostic text, never stack traces or raw payloads. Logs go to stderr so they never mix with the MCP protocol on stdout.

## Roadmap

- SMART on FHIR (OAuth 2.0) authentication
- Remote HTTP deployment on Azure Container Apps
- An MCP prompt that assembles a visit summary from several tools
