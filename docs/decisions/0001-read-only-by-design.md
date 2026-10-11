# 0001: The server is read-only

**Status:** Accepted, October 2026

## Context

An MCP server gives a language model the ability to act, not just to answer. With clinical data, the cost of a wrong action (an edited allergy, a cancelled order) is far higher than the cost of a wrong answer, which a person reads and can question. Models can also be steered by text they read, including text inside the records themselves.

## Options

1. Read and write, with confirmation prompts in the client.
2. Read and write, behind a separate flag.
3. Read only.

## Decision

Read only. The client issues only `GET` requests, every tool is annotated `ReadOnly = true` (reported to clients as `readOnlyHint`), and there is no code path that builds a `POST`, `PUT`, `PATCH` or `DELETE`.

## Consequences

- The question "what could this do to the record?" has a short answer: nothing. That makes the server easy to approve and easy to reason about.
- Workflows that end in an action (placing an order, updating a problem list) are out of scope. If write access is ever added, it belongs in a separate server with its own authorization, audit and review, not as a flag on this one.
- Read access still exposes data, so privacy and audit controls are still needed (see 0006).
