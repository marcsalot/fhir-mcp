# FHIR MCP Server

An MCP server, written in C# on .NET 8, that lets Claude and other MCP clients answer questions about patient records over FHIR R4. Ask "What medications is this patient on?" and the server runs the FHIR searches, trims the verbose responses down to what matters, and hands back a compact answer.

> **Synthetic data only.** This project points at public FHIR test servers seeded with synthetic patients. It is read-only and is not connected to any real patient data.

## Status

| Piece | State |
| --- | --- |
| FHIR client and patient search | Done |
| Tests (patient search) | Done |
| MCP server and tools | Next |
| Medications, conditions, encounters, observations | Planned |
| SMART on FHIR authentication | Stretch goal |

## How it works

```
Claude  -->  MCP server (this project)  -->  FHIR R4 server
                    |
                    +-- flattens Bundles into small summaries
                    +-- resolves references (for example, a medication ID to its name)
                    +-- caps and pages results
                    +-- turns FHIR errors (OperationOutcome) into plain messages
```

FHIR responses are large. A single medication search can run to thousands of lines, which wastes the model's context and makes answers worse. Most of the work in this project is in shaping those responses, not in calling the API.

### Design choices

- **No dependencies in the core library.** The FHIR client uses `HttpClient` and `System.Text.Json` and reads only the fields it needs, rather than loading a full FHIR object model. This keeps the output shaping explicit and easy to test.
- **Read-only, bounded queries.** Every search requires real criteria and caps its results (50 at most), so a tool call can never page through a whole server.
- **Honest data.** Partial birth dates (`1955`) are not turned into exact ages, and deceased patients get no current age.
- **Testable without a network.** Tests run against saved synthetic responses through a fake HTTP handler, and the clock is injected through `TimeProvider`.

## Project layout

```
src/FhirMcp.Core      FHIR client, search criteria, mapping to compact summaries
src/FhirMcp.Server    Smoke-test CLI today; the MCP host next
tests/FhirMcp.Tests   xUnit tests with synthetic FHIR fixtures
```

## Getting started

Requires the .NET 8 SDK.

```bash
dotnet build
dotnet test
```

Try the client against a live public test server (defaults to the SMART Health IT R4 sandbox):

```bash
dotnet run --project src/FhirMcp.Server -- search --name smith
dotnet run --project src/FhirMcp.Server -- search --birthdate 1970-01-01 --max 5
```

Use a different server by setting `FHIR_BASE_URL`, for example `https://hapi.fhir.org/baseR4/`.

## Security notes

- Read-only: the client only issues `GET` searches.
- Synthetic data only; do not point this at a server holding real patient data without adding authentication, audit logging, and a review of what is sent to the model.
- Error messages returned to the model contain the server's diagnostic text, never stack traces or raw payloads.

## Roadmap

- MCP server using the official C# SDK, with tools for patient search, a patient summary, medications, conditions, encounters, and observations
- Reference resolution and result paging
- SMART on FHIR (OAuth 2.0) authentication
- Remote HTTP deployment on Azure Container Apps
- An MCP prompt that assembles a visit summary from several tools
