# 0007: Sign in with SMART on FHIR, as a user, from the terminal

**Status:** Accepted, October 2026

## Context

Until now the server read from open test servers with no credentials. A real FHIR server requires authorization, and the healthcare standard for it is SMART on FHIR: OAuth 2.0 with FHIR-specific scopes (`patient/*.read`, `user/*.read`) and launch context (the patient chosen at sign-in). Authorization also answers a question the audit log could not: which person was signed in when a record was read.

Two facts shaped the design. First, this server runs in the background under an MCP client (Claude Desktop) over stdio, with no terminal of its own and no natural moment to open a browser. Second, it is a public client: it runs on a person's machine and cannot keep a client secret.

## Options

1. **Backend services** (client credentials with a signed JWT). The server signs in as itself. Simple to run unattended, but every call has the same broad system access and no person is accountable for it.
2. **Standalone launch as a user** (authorization code with PKCE), signing in once in a browser from a terminal command, then renewing tokens in the background.
3. **MCP's own authorization**, where the MCP client signs the user in to a remote MCP server. The right model once the server is hosted over HTTP, but this server runs locally over stdio, where that flow does not apply.

## Decision

Option 2, off by default (`FHIR_MCP_AUTH=smart` turns it on).

- **Sign-in is a terminal command** (`login`). It reads the server's `.well-known/smart-configuration`, opens the browser to the authorization endpoint, and receives the code on a loopback redirect (`http://127.0.0.1:<random port>/callback`, as RFC 8252 recommends for native apps). PKCE with S256 is required: discovery fails if the server does not offer it, so a stolen code is useless without the verifier that never leaves this process. A random `state` value is checked to reject forged redirects. `logout` and `auth-status` complete the set; `auth-status` never prints tokens.
- **The server only uses and renews tokens.** A `DelegatingHandler` adds the access token to every FHIR request, refreshes it 60 seconds before expiry, and, if the server still answers 401, refreshes once and retries. When no sign-in exists, tools return a message telling the user which command to run, and a sign-in made while the server is running is picked up without a restart.
- **Read scopes only.** Scopes that allow writing (`.write`, `.*`, or v2 letters other than `r` and `s`) are refused at startup, consistent with 0001.
- **Patient context is enforced here as well as by the server.** When every granted resource scope is a patient scope and sign-in chose a patient, the client refuses requests about any other patient before sending them, restricts patient search to that id, and the audit log records the refusal as `denied`. The SMART sandbox does not enforce scopes itself, and a client that relies only on the server cannot show what it would do against one that does not.
- **Tokens at rest.** On macOS, tokens go in a file encrypted with AES-256-GCM under a random key kept in the login Keychain; the key is passed to the `security` tool on standard input, not as a command-line argument. Elsewhere, a JSON file with owner-only permissions (0600). Both are replaced atomically on each save.
- **Audit.** Each audit record carries `user`, the signed-in person's `fhirUser` (for example `Practitioner/123`), read from the ID token.

## Consequences

- With sign-in on, every read is tied to a person, limited by what that person was granted, and recorded with who they were.
- Sign-in needs a terminal once, and again when the refresh token expires or is revoked. That is an extra step compared with an MCP client that signs users in itself.
- The ID token's signature is not verified. It arrives directly from the token endpoint over HTTPS, and `fhirUser` is used only to label audit records, never to grant access. If it were ever used for access decisions, the signature would need checking against the server's JWKS.
- `logout` deletes the saved tokens but cannot revoke them at the server: the sandbox has no revocation endpoint. Issued access tokens stay valid until they expire (one hour on the sandbox).
- The token file and the Keychain key protect against other users and casual copying, not against malware running as the same user, which could ask the Keychain for the key as this tool does.
- Testing against the sandbox has limits: it accepts requests with or without a token (it only rejects invalid ones) and does not enforce scopes. Live checks therefore confirm discovery, PKCE, the code exchange, refresh, retry after 401, and the client-side patient limit, but not a server's own enforcement.
- Remote HTTP hosting would replace the terminal sign-in with MCP's authorization flow (option 3). The token handling, scope checks and patient limit would carry over.
