# FHIR MCP Server

An MCP server, written in C# on .NET 10, that lets Claude and other MCP clients answer questions about patient records over FHIR R4. Ask "What is Reyna Ziemann being treated for, and do her medications line up with it?" and the server runs the FHIR searches, trims the verbose responses down to what matters, and hands back a compact answer.

> **Synthetic data only.** This project points at public FHIR test servers seeded with synthetic patients. It is read-only and is not connected to any real patient data.

## Why this exists

Connecting an AI model to patient records over FHIR is a natural idea. Making it work well is harder than it looks, for two reasons.

**FHIR search is more literal than it looks.** The standard `name=` parameter matches only the beginning of one stored name value, so searching for "Reyna Ziemann" returns nothing: the phrase has to match one value from the start. "Sayed" misses "Al-Sayed" because "Al-" comes first. "a1c" misses "Hemoglobin A1c/Hemoglobin.total in Blood" because the test name does not start with "a1c". These are not bugs; they are how the standard works. But they matter a lot when a model generates search terms from natural language.

**Raw FHIR responses are large.** A patient summary takes six parallel searches and returns about 117 KB of JSON for one synthetic patient, much of it metadata and coding structure. Sending that to a model wastes context and produces worse answers.

This project addresses both: name and lab search are done on our side, with transparent notes about how results were found, and each tool returns a compact summary shaped for a model rather than for a client that needs the full resource. The MCP protocol connects it to Claude and other AI clients.

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
| `Reyna Ziemann` | 0 results (the phrase must match one name value) | Reyna Ziemann |
| `Sayed` (for Al-Sayed) | 0 results (only the start of "Al-Sayed" matches) | Fatima Al-Sayed, flagged as a looser match |
| `fatima al sayed` | 0 results | Fatima Al-Sayed |
| `Hammes` | Melony Williamson, with no hint why | Melony Williamson, showing "Melony Hammes (maiden)" |

How it works:

1. The server is asked about the most specific word typed (the longest), as a prefix, with hyphens kept.
2. Every word typed is then checked against all of each candidate's names: official, maiden, nickname. Case, accents, hyphens and apostrophes are ignored, so `arisso` finds Arissó and `obrien` finds O'Brien.
3. If nothing matches, the search is retried with FHIR's `:contains` modifier, and the result says so.
4. Each result lists the patient's other names, so it is clear why they matched.

Known limit: a name typed without its hyphen or apostrophe (`AlSayed`, `okeefe`) is not found, because the server stores "Al-Sayed" and "O'Keefe" and offers no way to match across those characters. The tool's description and its no-match note tell the model to use the stored spelling.

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

Requires the .NET 10 SDK.

```bash
dotnet build
dotnet test
```

Try a search from the command line (defaults to the SMART Health IT R4 sandbox):

```bash
dotnet run --project src/FhirMcp.Server -- search --name "reyna ziemann"
dotnet run --project src/FhirMcp.Server -- search --name ham --contains
```

Use a different server by setting `FHIR_BASE_URL`, for example `https://hapi.fhir.org/baseR4/`.

### Test servers

Both servers below speak FHIR R4 (the version of the standard). They differ in who runs them and what data they hold.

| | SMART Health IT sandbox (default) | HAPI public test server |
| --- | --- | --- |
| URL | `https://r4.smarthealthit.org/` | `https://hapi.fhir.org/baseR4/` |
| Run by | The [SMART Health IT](https://smarthealthit.org/) project | The [HAPI FHIR](https://hapifhir.io/) open-source project, as a public demo |
| Data | Curated synthetic patients generated by [Synthea](https://synthetichealth.github.io/synthea/), with realistic histories | Whatever anyone uploads; records vary widely and are often sparse |
| Stability | Stable; patient ids stay valid | Data is added and cleared regularly; ids can disappear |
| Use it for | Demos and realistic questions (Reyna Ziemann is here) | Edge cases: hyphenated names, medications given by reference |

Both hold test data only. Never send real patient information to either one; the HAPI server is publicly writable and anything stored there is visible to anyone.

### Connect it to Claude Desktop

Build once, then in Claude Desktop open **Settings > Developer > Edit Config** and add the server, using the full path to the built program (run `pwd` in the project folder to get it):

```json
{
  "mcpServers": {
    "fhir": {
      "command": "/Users/you/Projects/fhir-mcp/src/FhirMcp.Server/bin/Debug/net10.0/FhirMcp.Server",
      "env": { "FHIR_BASE_URL": "https://r4.smarthealthit.org/" }
    }
  }
}
```

Fully quit and reopen Claude Desktop. A full path is more reliable than `dotnet run`, because desktop apps on macOS do not always share the terminal's PATH.

### Connect it to Claude Code

Build once, then register the server. Use `-e` to set the FHIR server URL:

```bash
# SMART Health IT sandbox (default, curated synthetic patients)
claude mcp add fhir -e FHIR_BASE_URL=https://r4.smarthealthit.org/ -- /path/to/fhir-mcp/src/FhirMcp.Server/bin/Debug/net10.0/FhirMcp.Server

# HAPI public test server (edge cases: hyphenated names, medication references)
claude mcp add fhir -e FHIR_BASE_URL=https://hapi.fhir.org/baseR4/ -- /path/to/fhir-mcp/src/FhirMcp.Server/bin/Debug/net10.0/FhirMcp.Server
```

Replace `/path/to/fhir-mcp` with the absolute path to this repo. To check what is registered:

```bash
claude mcp list
```

To remove and re-add with a different server:

```bash
claude mcp remove fhir
```

Once registered, ask Claude Code questions like: "Find Reyna Ziemann and summarize her health." or "Is her blood sugar trend improving?"

### Test it by hand

[MCP Inspector](https://github.com/modelcontextprotocol/inspector) gives you a web page to call each tool:

```bash
npx @modelcontextprotocol/inspector ./src/FhirMcp.Server/bin/Debug/net10.0/FhirMcp.Server
```

| Tool | Input | Expected |
| --- | --- | --- |
The main example patient is **Reyna Ziemann** (`76d2fa2d-66a6-4823-8dab-52cd70822844`): 7 active conditions including hypertension and prediabetes, 5 active medications, 10 allergies, 25 lab tests, and an A1c trend from 2014 to 2020.

| Tool | Input | Expected |
| --- | --- | --- |
| search_patients | name: `reyna ziemann` | 1 patient, Reyna Ziemann |
| search_patients | name: `Melony Hammes` | Melony Williamson, maiden name Hammes |
| search_patients | name: `o'keefe` | Mathilda and Clarine O'Keefe |
| get_patient_summary | Reyna's id | 7 conditions, 5 medications, 10 allergies, latest vitals, last visit 2020-05-27 |
| get_medications | Reyna's id, status: `active` | Hydrochlorothiazide, inhalers, ferrous sulfate, diphenhydramine |
| get_observations | Reyna's id, text: `a1c` | 5 results, 2014 to 2020, with a note that words were matched |
| get_observations | Reyna's id, category: `laboratory`, latestOnly: on | One result per test (25), including the lipid panel |
| get_observations | Reyna's id, category: `labs` | Error listing valid categories |
| search_patients | birthDate: `1/10/1953` | Error asking for yyyy-MM-dd |
| get_conditions | patientId: `../Patient` | Error saying the id is invalid |

Questions to try in Claude: "Find Reyna Ziemann and summarize her health." "Do her medications line up with her conditions?" "Is her blood sugar getting better or worse?" "Anything in her allergies to watch for?"

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
- **Testable without a network.** Tests run against synthetic FHIR responses through a fake HTTP handler, and the clock is injected through `TimeProvider`. 96 tests cover mapping, name and test matching, request shape, limits, errors, and the JSON each tool returns.

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
