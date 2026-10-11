# 0004: Keep the core library free of dependencies

**Status:** Accepted, October 2026

## Context

The core library talks to FHIR, matches names, maps resources and applies privacy rules. The established .NET option for FHIR is the Firely SDK, which provides a full object model, parsing and validation. The MCP SDK is a second candidate dependency, since the tools are MCP tools.

## Options

1. Firely SDK in the core; MCP SDK in the core.
2. Firely SDK in the core; MCP only in the server.
3. No packages in the core: `HttpClient` and `System.Text.Json` only; MCP only in the server.

## Decision

Option 3. The core reads the handful of fields each mapper needs straight from JSON, and knows nothing about MCP.

## Consequences

- What reaches the model is decided field by field in code that is short and fully tested, rather than by walking a large object model.
- The core could sit behind a REST API, a different AI tool protocol, or a batch job without change. The MCP server is a thin layer: tool descriptions, argument parsing and policy enforcement.
- No validation of incoming resources. A malformed resource produces missing fields, not an error. For a read-only summarizer this is acceptable; a system that writes or relies on full fidelity should use a proper FHIR library.
- New resource types need a hand-written mapper. So far each has been under 100 lines.
