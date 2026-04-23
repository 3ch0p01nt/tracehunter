# TraceHunter

> Open-source .NET-native EDR for Windows. Programmable ETW threat hunting in a single self-contained executable.

**Status:** Phase 0 - bootstrap. Not yet functional. See [the design doc](docs/plans/2026-04-22-tracehunter-design.md) for the full v1.0 plan.

## What it is

TraceHunter turns Event Tracing for Windows (ETW) into a real-time threat-hunting platform. It captures kernel and user-mode events from seven providers, normalizes them, evaluates them against Sigma-format and native YAML rules, maintains a live process provenance graph, and exposes everything through an embedded local web UI - all in one `tracehunter.exe`.

## Why not just use Sysmon

- Sysmon's XML config is painful; TraceHunter speaks Sigma and a clean YAML DSL.
- Sysmon has no .NET runtime visibility; TraceHunter does (`Microsoft-Windows-DotNETRuntime`).
- Sysmon needs a driver; TraceHunter doesn't.
- Sysmon emits to the event log and stops there; TraceHunter ships rules, detections, and a UI in the box.

## Quickstart

> Coming in Phase 8. For now, this repo only builds and tests.

```
dotnet build
dotnet test
```

## Architecture

See [`docs/plans/2026-04-22-tracehunter-design.md`](docs/plans/2026-04-22-tracehunter-design.md) for the full design and [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the distilled overview (lands in Task 17).

## License

Apache 2.0 - see [LICENSE](LICENSE).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).
