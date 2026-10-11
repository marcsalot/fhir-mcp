# 0005: Tools return JSON text, not typed objects

**Status:** Accepted, October 2026

## Context

The C# MCP SDK (version 2.2.0) can return typed objects as structured content, generating an output schema from the return type. In testing with MCP Inspector, the schema generator leaked response properties into the tool's input schema: for example, `note` from the search response appeared as a required input field, and calls were rejected as invalid.

## Options

1. Keep typed returns and work around the generator with attributes.
2. Wait for an SDK fix.
3. Return JSON text built by one shared serializer.

## Decision

Option 3. Every tool returns a string produced by `ToolSerializer` (camelCase, nulls omitted, non-ASCII left readable), and `SerializationTests` pins the output shape of every response type. Since 0006, tools serialize through `ToolOutput`, which applies the privacy mode on top of the same serializer.

## Consequences

- Input schemas are exactly the tool parameters, and every client tested accepts them.
- Clients do not receive a machine-readable output schema. Models read the JSON text without one, so in practice nothing is lost for this use.
- One serialization path makes privacy enforcement simpler: there is a single place where a response becomes text (see 0006).
- Revisit when the SDK's structured output works for these types.
