# FHIR MCP Server

An MCP server, written in C# on .NET 8, that lets Claude and other MCP clients answer questions about patient records over FHIR R4. Ask "Find Adolph Smith" and the server runs the FHIR searches, trims the verbose responses down to what matters, and hands back a compact answer.

> **Synthetic data only.** This project points at public FHIR test servers seeded with synthetic patients. It is read-only and is not connected to any real patient data.

## Status

| Piece | State |
| --- | --- |
| FHIR client and patient search | Done |
| Name matching (full names, hyphens, accents, maiden names, fallback) | Done |
| MCP server with `search_patients` | Done |
| Medications, conditions, encounters, observations | Next |
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

FHIR responses are large. A single medication search can run to thousands of lines, which wastes the model's context and makes answers worse. Most of the work in this project is in shaping requests and responses, not in calling the API.

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
| `search_patients` | Finds patients by any part of the name and/or birth date. Returns id, name, other names, gender, birth date, age, city, state, and a deceased flag. |

Tools are read-only and annotated as such. Errors (a bad date, a server error) come back as messages the model can act on.

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
- **Testable without a network.** Tests run against saved synthetic responses through a fake HTTP handler, and the clock is injected through `TimeProvider`.

## Security notes

- Read-only: the client only issues `GET` searches.
- Synthetic data only; do not point this at a server holding real patient data without adding authentication, audit logging, and a review of what is sent to the model.
- Error messages returned to the model contain the server's diagnostic text, never stack traces or raw payloads. Logs go to stderr so they never mix with the MCP protocol on stdout.

## Roadmap

- Tools for a patient summary, medications, conditions, encounters, and observations, with reference resolution and paging
- SMART on FHIR (OAuth 2.0) authentication
- Remote HTTP deployment on Azure Container Apps
- An MCP prompt that assembles a visit summary from several tools
