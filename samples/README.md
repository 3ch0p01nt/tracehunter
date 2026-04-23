# TraceHunter samples

Contents:

- `rules/` - bundled detection rules (Sigma format and native YAML DSL). Populated in Phase 9.
- `scripts/` - demo and smoke-test scripts. Populated in Phase 9 and 10.

Rules in this directory are loaded by default when TraceHunter starts. Add custom rules to `%PROGRAMDATA%\TraceHunter\rules\` (service mode) or `%LOCALAPPDATA%\TraceHunter\rules\` (interactive mode) - those locations are scanned in addition to the bundled set.
