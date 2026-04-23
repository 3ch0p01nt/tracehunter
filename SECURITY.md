# Security Policy

## Reporting a vulnerability

Please **do not** open a public issue for security vulnerabilities.

Email: `rsoligan@gmail.com` with the subject line `TraceHunter security: <short title>`. Include reproduction steps, impact assessment, and suggested mitigation if you have one.

You'll get an acknowledgement within 72 hours and a status update within 14 days.

## Threat model (summary)

TraceHunter runs with elevated privileges to access kernel ETW providers. Its threat surface includes:

- Local code execution by an attacker with admin rights (out of scope - they already win).
- Malicious detection rules (in scope - rule loader will be hardened against directory-traversal and resource-exhaustion in v1.0).
- Web UI bound to `127.0.0.1` only (in scope - must never bind to a routable interface without explicit operator opt-in and authentication).
- Persistence of sensitive data (in scope - `data.db` may contain command lines and script blocks; documented in OPERATIONS.md).

Full threat model lands in `docs/SECURITY.md` in Phase 10.
