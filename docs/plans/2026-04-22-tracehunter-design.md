# TraceHunter — Design Document

**Date:** 2026-04-22
**Status:** Approved (brainstorming complete; implementation plan to follow)
**Owner:** Robert Soligan

---

## 1. Vision & positioning

**TraceHunter** is an open-source, .NET-native EDR for Windows that turns Event Tracing for Windows (ETW) into a programmable threat-hunting platform. It ships as a single self-contained executable that captures kernel and user-mode ETW events, normalizes and enriches them, evaluates them against Sigma-format and native YAML detection rules in real time, maintains a live process provenance graph, and surfaces the whole thing in a local web UI.

**One-liner:** Sysmon-class telemetry plus the things Sysmon will never have (CLR runtime visibility, programmable rules, no driver), wrapped in a live process-provenance UI.

**Why now:** Sysmon's XML config is painful, its rule expression is limited, and it has no visibility into the .NET runtime. Velociraptor/Wazuh/Elastic Security require server infrastructure. Defender's hunting surface is locked behind Microsoft 365. Nothing in the OSS landscape gives a single-host analyst a programmable, .NET-aware, install-and-run-in-five-minutes experience.

## 2. Goals & non-goals

### Goals (v1.0)
- Single self-contained `tracehunter.exe`; clone-and-run in <5 minutes for a stranger.
- Capture seven event sources: Process, ImageLoad, Network, PowerShell, CLR, DNS, WMI.
- Real-time evaluation of Sigma YAML and a native YAML DSL; ship 15+ curated rules.
- Live process provenance graph in an embedded Blazor Server UI on `localhost`.
- Both interactive (`tracehunter run`) and Windows Service modes.
- Performance budget: <5% CPU and <300 MB RAM at idle, hard cap configurable.
- Apache 2.0 licensed; runs on .NET 8 LTS, Windows x64 and ARM64.

### Non-goals (v1.0)
- Multi-host / fleet management — post-v2 phase.
- File I/O and Registry providers — v1.2 (need careful sampling design).
- Stack walking + symbol resolution — v1.3.
- C# plugin model for detection — v1.1 (scaffolding in v1.0; no loader).
- ETLX cold archive — v1.1.
- Auto-update infrastructure — v1.2.
- Code signing — v1.1.
- macOS / Linux — out of scope (ETW is Windows-only).
- Active response (kill process, quarantine) — out of scope; this is a sensor + analyst tool, not a blocking EDR.

## 3. High-level architecture

```
                 Windows OS
                     |
              ETW Real-Time Sessions
        +------------+------------+
        |                         |
   KernelSessionHost      UserSessionHost
   (Process, ImageLoad,   (PowerShell, CLR,
    Network — admin)        DNS, WMI — any)
        |                         |
        +-----------+-------------+
                    |
              Raw Event Channel
                    |
            Normalization Layer
       (per-provider parsers -> NormalizedEvent)
                    |
              Enrichment Layer
       (process tree, signing, DNS<->net correlation)
                    |
        +-----------+-------------+
        |           |             |
   Provenance   Detection      Storage Tier
     Graph       Engine        Hot ring buffer
   (in-mem)   (rules->find.)   Warm SQLite
        |           |             |
        +-----------+-------------+
                    |
              SignalR Hub
                    |
         Blazor Server Web UI
       (Kestrel on localhost:port)
                    |
                  Browser
```

**Single-process model.** Capture, normalization, enrichment, storage, detection, and UI all live in one executable, sharing memory. No IPC, no separate collector daemon, no agent-to-server protocol in v1.

## 4. Components

### 4.1 Capture layer (`TraceHunter.Capture`)

Owns two `Microsoft.Diagnostics.Tracing.Session.TraceEventSession` instances:
- **`KernelSessionHost`** — kernel session for `Microsoft-Windows-Kernel-Process`, `Microsoft-Windows-Kernel-Image`, `Microsoft-Windows-Kernel-Network`. Requires admin (SeDebugPrivilege + SeSystemProfilePrivilege).
- **`UserSessionHost`** — user-mode session for `Microsoft-Windows-PowerShell` (script-block, ID 4104), `Microsoft-Windows-DotNETRuntime`, `Microsoft-Windows-DNS-Client`, `Microsoft-Windows-WMI-Activity`. Runs unprivileged.

Both feed a single `BoundedChannel<RawEvent>` (default capacity 100k) with the configured backpressure policy (see §8). Sessions started, monitored, and restarted with exponential backoff per provider on failure (max 5 attempts).

### 4.2 Normalization layer (`TraceHunter.Normalization`)

Per-provider parser modules translate raw ETW events into a unified `NormalizedEvent` discriminated union:

```
NormalizedEvent
  | Process     { create/exit, image, cmdline, ppid, integrity, user_sid }
  | ImageLoad   { image_path, base_addr, size, signed?, signer? }
  | Network     { protocol, local/remote endpoints, direction }
  | Script      { source, script_block_id, content, deobfuscated? }
  | Runtime     { exception | gc | jit | assembly_load | thread_pool }
  | Dns         { query | response, name, type, result }
  | Wmi         { provider, class, method, args }
```

Common envelope on every event: `timestamp`, `pid`, `ppid`, `process_image`, `user_sid`, `thread_id`, `host`. Parsers are pure, allocation-conscious, individually unit-testable.

### 4.3 Enrichment layer (`TraceHunter.Enrichment`)

Side-effect-free augmentations applied after normalization:
- **Process ancestry resolution** — walks parent chain from in-memory process index.
- **Authenticode signing status** — caches by `(file_path, mtime)`; cache miss costs ~2 ms once per binary.
- **DNS<->Network correlation** — matches a `Network.Connect` to the most recent `Dns.Response` from the same PID within a 60-second window.

Misses are non-fatal: events flow through with enrichment fields null.

### 4.4 Provenance graph (`TraceHunter.Core` + maintained in `TraceHunter.Enrichment`)

In-memory `ConcurrentDictionary<int, ProcessNode>` updated from `Process` events. Each `ProcessNode` holds image path, command line, signing, parent reference, child references, recent event counters by category, start/exit timestamps. UI subscribes to mutations via SignalR. Snapshot reads use a copy-on-write index for lock-free traversal.

### 4.5 Storage tier (`TraceHunter.Storage`)

**Hot — ring buffer.** `RingBuffer<NormalizedEvent>` (default 5 minutes / 100k events, whichever smaller) for the live UI's "scroll back without hitting SQLite" UX. Drop-oldest eviction with a counter exposed in the Settings page.

**Warm — SQLite.** Database at `%PROGRAMDATA%\TraceHunter\data.db` (service mode) or `%LOCALAPPDATA%\TraceHunter\data.db` (interactive). WAL mode, `synchronous=NORMAL`, page size 16 KB. Writes batched (default 500 events / 250 ms) by a dedicated writer task. Schema:

```sql
CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT);

CREATE TABLE events (
    id INTEGER PRIMARY KEY,
    ts INTEGER NOT NULL,
    category TEXT NOT NULL,
    pid INTEGER NOT NULL,
    ppid INTEGER,
    process_image TEXT,
    user_sid TEXT,
    payload_json TEXT NOT NULL
);
CREATE INDEX ix_events_ts ON events(ts);
CREATE INDEX ix_events_pid_ts ON events(pid, ts);
CREATE INDEX ix_events_category_ts ON events(category, ts);

CREATE TABLE findings (
    id TEXT PRIMARY KEY,
    ts INTEGER NOT NULL,
    rule_id TEXT NOT NULL,
    severity TEXT NOT NULL,
    mitre_json TEXT NOT NULL,
    primary_event_id INTEGER NOT NULL,
    evidence_event_ids_json TEXT NOT NULL,
    FOREIGN KEY (primary_event_id) REFERENCES events(id)
);
CREATE INDEX ix_findings_ts ON findings(ts);
CREATE INDEX ix_findings_rule ON findings(rule_id, ts);

CREATE TABLE process_snapshots (
    pid INTEGER NOT NULL,
    started_ts INTEGER NOT NULL,
    exited_ts INTEGER,
    image TEXT,
    cmdline TEXT,
    parent_pid INTEGER,
    user_sid TEXT,
    signing_status TEXT,
    PRIMARY KEY (pid, started_ts)
);
```

Retention: hourly background sweeper deletes events older than configured TTL (default 7 days). VACUUM nightly during low-activity windows.

**Cold (v1.1)** — rolling ETLX archive: every N minutes a chunk of raw events is rotated into a `.etlx` file. Investigation UI loads chunks on demand. PerfView/WPA become free advanced viewers.

### 4.6 Detection engine (`TraceHunter.Detection`)

**Rule sources.** Two formats parsed into a unified internal `IRule`:

1. **Sigma YAML** — community-standard format. Parser maps Sigma's `logsource` taxonomy onto the normalized event categories:

| Sigma logsource | TraceHunter category |
|---|---|
| `process_creation` | `Process` |
| `image_load` | `ImageLoad` |
| `network_connection` | `Network` |
| `dns` | `Dns` |
| `ps_script` | `Script` |
| `wmi_event` | `Wmi` |
| (no equivalent) | `Runtime` (native DSL only) |

Modifiers supported in v1: `contains`, `startswith`, `endswith`, `regex`, `base64`. Aggregation/correlation deferred — rare in shipped Sigma packs.

2. **Native YAML DSL** — designed around the TraceHunter event model. Sample:

```yaml
id: th.process.office_spawns_shell
title: Office application spawned a shell
severity: high
mitre: [T1059.001, T1566.001]
match:
  type: Process
  where:
    parent_image: [winword.exe, excel.exe, powerpnt.exe, outlook.exe]
    image: [powershell.exe, pwsh.exe, cmd.exe, wscript.exe, cscript.exe]
evidence:
  include_ancestry: 3
```

```yaml
id: th.runtime.lsass_managed_exception_storm
title: Managed exception storm in lsass.exe
severity: critical
mitre: [T1003.001]
match:
  type: Runtime.Exception
  where:
    process_image: lsass.exe
  window:
    count_gte: 25
    within: 5s
```

```yaml
id: th.image.signed_process_loads_unsigned_dll
title: Signed process sideloaded an unsigned DLL
severity: medium
mitre: [T1574.002]
match:
  type: ImageLoad
  where:
    process_signed: true
    image_signed: false
    image_path_not_in: [C:\Windows\, C:\Program Files\, C:\Program Files (x86)\]
```

DSL grammar in v1:
- `match.type` (required) — event category.
- `match.where` — equality, lists (`field: [a, b]`), `_in` / `_not_in`, `_contains`, `_regex`.
- `match.window` (optional) — `count_gte` + `within: <duration>` for stateful sequence rules; group key = PID by default, overridable.
- `evidence.include_ancestry` (optional) — N levels of process ancestry attached to the finding.

Rules validated at load time against a JSON Schema; failures surface in the Rules UI page with filename + line.

**Evaluation model.** Rules grouped by event type — an event walks only rules registered for its category. Stateless rules are pure predicates (~1-3 µs each). Stateful rules maintain per-rule sliding-window buffers keyed by group fields; events age out past the window.

**Findings** persisted to SQLite, pushed to UI live, emitted as newline-delimited JSON to a configurable sink (file, stdout, or HTTP webhook) for SIEM forwarding.

### 4.7 Web UI host (`TraceHunter.Web`)

Embedded Kestrel binds `127.0.0.1:<port>` (default 7777, falls back to next free port). Blazor Server pages render real-time views; SignalR hub pushes mutations on three channels: `graph`, `findings`, `events` (events rate-limited to 100/sec to UI). Static assets embedded in the binary as resources.

**Pages:**

| Page | Purpose | Key widgets |
|---|---|---|
| Overview | Landing page | Findings counter (24h by severity), provider health, current event rate, top noisy processes |
| Process Tree | Live provenance graph (headline) | Cytoscape.js render, click-to-detail, filter by signing/user/depth |
| Findings | Detection stream | Virtualized list, severity + MITRE filter, evidence chain expand |
| Event Stream | Live event tail | Virtualized log view, filter by category/PID, pause/resume, copy-as-JSON |
| Rules | Manage detection rules | Grouped by source, toggle enabled, view source YAML, hot-reload, validation errors |
| History | Query SQLite warm storage | Time picker, structured filters, JSON/CSV export |
| Settings | Runtime config | Provider toggles + degradation, sampling thresholds, retention, sink config |

### 4.8 Service shell (`TraceHunter.Host`)

`Microsoft.Extensions.Hosting` host wrapping all of the above. CLI verbs:

- `tracehunter run [--port N] [--config PATH]` — interactive; opens browser; foreground.
- `tracehunter install-service` — registers Windows Service `TraceHunter`, auto-start. Admin required.
- `tracehunter uninstall-service` — removes service. Data directory left in place; warning printed with path.
- `tracehunter capture --raw` — emit raw events to stdout (debug aid).
- `tracehunter capture` — emit normalized JSON to stdout (debug aid).
- `tracehunter tree` — dump current process tree (debug aid).
- `tracehunter query --since <duration> [--category C] [--pid P]` — query SQLite (debug aid).

Sensible defaults — `tracehunter run` works zero-config.

## 5. Data flow — single event lifecycle

1. Kernel ETW emits `Process/Start` for `notepad.exe`.
2. `KernelSessionHost` reads it, wraps as `RawEvent`, writes to channel (~5 µs).
3. Normalizer pulls it, parses into `NormalizedEvent.Process` (~10 µs, no allocations beyond the struct).
4. Enricher attaches Authenticode signing status (cache hit ~1 µs, miss ~2 ms once per binary).
5. Event fan-outs to three consumers in parallel:
   - **Provenance graph** — adds `ProcessNode`, links to parent, signals SignalR.
   - **Storage** — appended to ring buffer; batched into SQLite within 250 ms.
   - **Detection engine** — evaluated against rules registered for `Process`; if a match, `Finding` written and pushed to UI.
6. Browser sees the new node within ~250 ms.

Target end-to-end: <100 ms from ETW emit to UI render at idle load. Sampling kicks in if backlog > 10k events.

## 6. Performance budget

| Metric | Target | Hard cap |
|---|---|---|
| CPU at idle | <2% | <5% |
| CPU under burst | <8% | <15% (configurable) |
| RAM at idle | <150 MB | <300 MB |
| Event-to-UI latency (idle) | <100 ms | <500 ms |
| Disk I/O (steady state) | <5 MB/min | <20 MB/min |
| SQLite size at 7-day retention | <500 MB typical workload | (no hard cap; documented) |

Architecture choices supporting this:
- Channel-based pipeline keeps producers and consumers decoupled.
- Per-provider sampling kicks in before hard caps are hit.
- `Span<T>` and pooled buffers in hot-path normalization.
- Batched SQLite writes; WAL mode; no per-event flushes.
- Lazy enrichment for fields the UI doesn't currently need.

## 7. Resilience & error handling

### 7.1 Backpressure under load — three-tier policy

| Tier | Trigger | Action |
|---|---|---|
| 1 — Normal | Channel <70% full | All events processed |
| 2 — Sample | Channel 70-90% full | Sample non-detection-relevant events (DNS responses, ImageLoad of well-known system DLLs); always keep Process, Findings, Network connect |
| 3 — Drop | Channel >90% full | Drop-oldest, increment drop counter, surface UI banner |

Sustained Tier 3 for >60s emits a self-finding (`th.self.backpressure`) so analysts know they may have visibility gaps.

### 7.2 Provider failure isolation

Each provider session is independent. Failure in one (e.g. CLR provider manifest issue) does not stop others. UI Settings page shows red status with the underlying exception. Restart attempted with exponential backoff (max 5 attempts) then marked permanently failed until config reload.

### 7.3 Privilege degradation

Startup probe: try to start kernel session. If `Access is denied`, log it, skip kernel-only providers, run the rest. UI banner on first load: "Running in user-mode. N of 7 providers active. <list> disabled."

### 7.4 Rule load failures

Per-rule parse errors collected into `RuleLoadDiagnostics`, shown on Rules page (filename, line, error). Failed rules don't load; rest continue. Hot-reload re-runs the scan.

### 7.5 Browser disconnects

SignalR default reconnect policy. Server-side state is per-connection only. UI uses optimistic local cache, re-syncs on reconnect.

### 7.6 Dirty shutdown recovery

WAL handles partial writes. Ring buffer is lossy by design. On startup: rebuild provenance graph for processes still running by cross-checking `process_snapshots` against the live OS process list. Log: "Recovered N processes from snapshot, M events flushed since last clean shutdown."

### 7.7 Self-protection

TraceHunter PIDs excluded from rule evaluation by default (configurable) to prevent feedback loops. SQLite handles opened with `FileShare.Read` so snapshots can be taken without stopping the service.

## 8. Testing strategy

### 8.1 Unit (xUnit; target 70% coverage of non-UI code)
- Per-provider parsers: feed canned `TraceEvent` instances, assert `NormalizedEvent`.
- Rule engine evaluator: synthetic events through synthetic rules, assert findings.
- Sigma + native DSL parsers: positive and negative fixtures.
- Storage layer: SQLite roundtrips, retention sweeps, WAL recovery.

### 8.2 Integration (xUnit + synthetic ETW source)
- Spin up real ETW session against a custom `EventSource` we control. Assert end-to-end pipeline produces expected findings. Avoids depending on real OS providers in CI.
- Detection regression suite: each shipped rule has paired `*.events.json` and `*.expected.json`.

### 8.3 End-to-end smoke (Windows runner)
- GitHub Actions on `windows-latest`: build → unit → integration on every PR.
- Real-provider smoke nightly only (CI runners have lower trust).
- Local `scripts/smoke.ps1` triggers a known-bad scenario (`powershell -EncodedCommand <safe payload>`), asserts the corresponding finding.

### 8.4 UI
- bUnit component tests for non-trivial Blazor components.
- No Selenium/Playwright in v1.

## 9. Distribution & install

**Build artifacts (per release):**
- `tracehunter.exe` — self-contained single-file, win-x64, ~80 MB, runtime embedded.
- `tracehunter-arm64.exe` — same for ARM64 Windows.
- `tracehunter-<version>.zip` — exe + sample rules + LICENSE + README.
- `SHA256SUMS.txt` — checksums.

**Install paths:**

| Path | Command | Lifecycle |
|---|---|---|
| Try it | Download zip, extract, `tracehunter.exe run` | Foreground process, browser auto-opens, Ctrl-C exits |
| Always-on | `tracehunter.exe install-service` (admin) | Windows Service, auto-start, browser opens to `localhost:7777` on demand |
| Uninstall | `tracehunter.exe uninstall-service` | Service removed; data directory left; path printed |

**Data directory:** `%PROGRAMDATA%\TraceHunter\` (service) or `%LOCALAPPDATA%\TraceHunter\` (interactive). Contains `config.yaml`, `rules/`, `data.db`, `logs/`.

**Code signing:** Unsigned in v1.0, with documented "right-click → Properties → Unblock" workaround. SignPath.io free OSS tier or Sectigo cert pursued in v1.1.

**Auto-update:** Not in v1. Manual download-and-replace. Reconsider in v2.

## 10. Roadmap

| Version | Ships |
|---|---|
| **v1.0** | This document — 7 providers, hot+warm storage, Sigma + native DSL with 15+ rules, Blazor UI with 7 pages, interactive + service mode, single-file binary, Apache 2.0, GitHub Actions CI |
| **v1.1** | C# plugin model, ETLX cold archive, code signing, sample rule pack expansion (50+ rules) |
| **v1.2** | File I/O + Registry providers (with sampling), automated update channel |
| **v1.3** | Stack walking + symbol resolution on selected events, flame graph view |
| **v2.0** | Multi-host fleet — agent-server protocol, central UI, fleet-wide rule push, RBAC |

## 11. Repo layout

```
tracehunter/
  LICENSE                          (Apache 2.0)
  README.md
  CONTRIBUTING.md
  CODE_OF_CONDUCT.md
  TraceHunter.sln
  Directory.Build.props
  global.json
  .editorconfig
  .gitignore

  src/
    TraceHunter.Core/              domain types
    TraceHunter.Capture/           ETW sessions, raw event channel
    TraceHunter.Normalization/     per-provider parsers
    TraceHunter.Enrichment/        process tree, signing, DNS<->net correlation
    TraceHunter.Detection/         rule loader (Sigma + native DSL), evaluator
    TraceHunter.Storage/           ring buffer, SQLite, retention sweeper
    TraceHunter.Web/               Blazor Server, SignalR, Cytoscape interop
    TraceHunter.Host/              service shell, CLI, single-file entry

  tests/
    TraceHunter.Core.Tests/
    TraceHunter.Capture.Tests/
    TraceHunter.Normalization.Tests/
    TraceHunter.Enrichment.Tests/
    TraceHunter.Detection.Tests/
    TraceHunter.Storage.Tests/
    TraceHunter.Web.Tests/         (bUnit)
    TraceHunter.Integration.Tests/ (synthetic EventSource end-to-end)

  samples/
    rules/                         15+ bundled detection rules (.yaml)
    scripts/
      smoke.ps1
      demo-scenarios.md

  docs/
    README.md
    ARCHITECTURE.md
    RULE-AUTHORING.md
    OPERATIONS.md
    SECURITY.md
    plans/
      2026-04-22-tracehunter-design.md   (this file)

  .github/
    workflows/
      ci.yml
      release.yml
      codeql.yml
    ISSUE_TEMPLATE/
      bug.yml
      detection-request.yml
      feature.yml
    PULL_REQUEST_TEMPLATE.md
    dependabot.yml
```

## 12. v1.0 phase plan

| Phase | Deliverable | "Done" looks like |
|---|---|---|
| 0 — Bootstrap | Repo, solution, CI, .editorconfig, LICENSE, baseline README, Directory.Build.props | `dotnet build` passes on a fresh clone; CI green |
| 1 — Capture foundation | `Capture` + `Core` projects: kernel + user session hosts, raw event channel, graceful privilege fallback | `tracehunter capture --raw` prints raw events; integration test against synthetic EventSource passes |
| 2 — Normalization | All 7 provider parsers, full `NormalizedEvent` shape, unit tests per parser | `tracehunter capture` prints normalized JSON; parser test coverage >80% |
| 3 — Enrichment + provenance graph | Process tree, signing cache, DNS-net correlation, snapshot API | `tracehunter tree` dumps current process tree with signing + cmdlines |
| 4 — Storage | Ring buffer + SQLite + retention sweeper + dirty-shutdown recovery | Events persisted; `tracehunter query --since 1h` returns results; restart recovers tree |
| 5 — Detection engine | Sigma + native DSL parsers, rule evaluator, stateful window buffers, finding emission | `tracehunter detect --rule samples/rules/office-spawns-shell.yaml` against captured events emits findings |
| 6 — Web UI scaffolding | Blazor Server host, layout, Overview + Settings pages, SignalR hub, embedded assets | `tracehunter run` opens browser, shows live host + perf gauges |
| 7 — Web UI feature pages | Process Tree (Cytoscape), Findings, Event Stream, Rules, History pages | All 7 pages working with live data |
| 8 — Service shell | `install-service` / `uninstall-service` / `run` / config / port flags / single-file publish | `dotnet publish -r win-x64` produces `tracehunter.exe`; service installs, runs, persists across reboot |
| 9 — Rule pack v1 | 15+ detection rules covering process, image, network, dns, powershell, runtime; per-rule tests | All rules in `samples/rules/` parse, all have green tests |
| 10 — Launch polish | README with screenshots, animated GIF demo, CONTRIBUTING, ARCHITECTURE.md, release.yml, draft v1.0.0 release | Tag triggers release; README screenshots load; stranger can clone-and-run in 5 minutes |

Phases 1-5 ship together as a "headless v0.5 preview" if early signal is needed before the UI lands.

## 13. Decisions log

| Decision | Choice | Rationale |
|---|---|---|
| Audience | OSS standalone product | Broad distribution; user choice |
| Positioning | .NET-native EDR with full provenance graph | Demo wow-factor; differentiator vs Sysmon |
| UI surface | Local web dashboard, embedded Blazor Server | Real graph viz; single binary; no Node toolchain |
| Rule formats | Sigma + native YAML DSL | Sigma rides ecosystem; native DSL covers CLR/sequence rules Sigma can't |
| Storage | Hot in-memory + warm SQLite (v1); cold ETLX (v1.1) | Each tier optimized for its workload |
| MVP providers | Process, ImageLoad, Network, PowerShell, CLR, DNS, WMI | Credible EDR coverage minus the high-volume kernel firehose |
| Performance budget | <5% CPU / <300 MB RAM, hard cap configurable | Realistic for solo OSS dev; production-safe claim |
| Runtime | .NET 8 LTS | Supported through Nov 2026; AOT-capable for future |
| License | Apache 2.0 | Patent grant matters for security tooling |
| Name | TraceHunter | Descriptive, googleable, brandable |
| Privilege model | Graceful degradation | Wins non-admin analysts on first try |
| Run model | Both interactive + Windows Service | Try-it-fast UX + always-on production UX |
| Publish model | Self-contained single-file with runtime | No .NET install required; survives plugin model |
| Scope | Single-host v1; multi-host post-v2 | Bounds work; ships realistic timeframe |
| Detection latency | In-process on live event stream | Required for sub-second findings |
| Backpressure | Three-tier sample/drop with self-finding on sustained drop | Operators get visibility into gaps |
| Code signing v1 | Unsigned, documented workaround | Standard for early OSS; signing pursued v1.1 |
