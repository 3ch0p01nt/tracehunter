# Authoring TraceHunter Detection Rules

This document is a placeholder. Rule format design is defined in [`plans/2026-04-22-tracehunter-design.md`](plans/2026-04-22-tracehunter-design.md) §4.6.

Once the rule engine ships in Phase 5, this document will cover:

- Sigma YAML rules — supported logsources, modifiers, and TraceHunter mappings
- Native YAML DSL — full grammar reference with examples
- Testing rules — paired `*.events.json` / `*.expected.json` fixtures
- Loading and hot-reloading rules
- Best practices for low false-positive detections
