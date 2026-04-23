# Contributing to TraceHunter

Thanks for your interest. TraceHunter is in early-stage development; the design is locked but most code hasn't been written yet. The best contributions right now are detection rules, bug reports against the scaffolding, and feedback on the design doc.

## Before you open a PR

1. Read [`docs/plans/2026-04-22-tracehunter-design.md`](docs/plans/2026-04-22-tracehunter-design.md) - it captures every architectural decision and the v1.0 phase plan.
2. Check open issues - a maintainer may already be working on the area.
3. For non-trivial changes, open an issue first to discuss.

## Development setup

```
git clone <this repo>
cd tracehunter
dotnet build
dotnet test
```

Required: .NET 8 SDK (pinned via `global.json`), Windows 10/11 x64 or ARM64.

## Conventions

- All public APIs nullable-annotated.
- Warnings are errors (`TreatWarningsAsErrors=true`).
- Conventional Commits for messages (`feat:`, `fix:`, `docs:`, `test:`, `build:`, `ci:`, `chore:`).
- One logical change per commit; one logical change per PR.
- New detection rules require: a MITRE technique tag, a paired `*.events.json` test fixture, and a paired `*.expected.json` expected outcome.

## Code review

A maintainer reviews every PR. Expect questions; we want long-term contributors, not just merged PRs.
