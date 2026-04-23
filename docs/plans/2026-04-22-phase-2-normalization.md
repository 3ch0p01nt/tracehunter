# TraceHunter Phase 2 - Normalization Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Turn `RawEvent` into typed `NormalizedEvent` variants the rest of the system can reason about. After this phase, `tracehunter capture` (no flag) emits structured JSON per-provider — Process events look like Process, Network events look like Network — instead of the opaque payload-bag JSON Phase 1 produced.

**Architecture:** Per-provider parsers (one per `ProviderId`) translate `RawEvent` payloads into the appropriate `NormalizedEvent` variant. A `NormalizationStage` reads from the raw channel, dispatches each event to its parser, writes results to a normalized channel. The session hosts (`UserSessionHost`, `KernelSessionHost`) keep producing `RawEvent`s as before — no breaking change. `CaptureCoordinator` exposes both the raw and normalized readers so callers can pick. The CLI gains a default `tracehunter capture` (normalized) alongside the existing `tracehunter capture --raw`.

**Tech Stack:** .NET 8, `Microsoft.Diagnostics.Tracing.TraceEvent` 3.2.2, `System.Threading.Channels`, `System.Text.Json`. All Phase 1 dependencies.

---

## Context for the executor

You're implementing Phase 2 of TraceHunter. Read these before starting:
- `docs/plans/2026-04-22-tracehunter-design.md` Section 4.2 (Normalization layer) and Section 5 (Data flow).
- `docs/plans/2026-04-22-phase-1-capture-foundation.md` for Phase 1 shape.
- The existing `RawEvent`, `ProviderId`, `UserSessionHost`, `KernelSessionHost`, `CaptureCoordinator` source — Phase 2 builds on top of all of them without breaking any.

Phase 1 is merged to main. State at start of Phase 2:
- `RawEvent` envelope wraps any ETW event. Its `PayloadJson` is a JSON-serialized dictionary of payload fields (the `UserSessionHost` does this in `SerializePayload`; the `KernelSessionHost` builds a typed object then serializes).
- `CaptureCoordinator` constructs `KernelSessionHost` and `UserSessionHost`, feeds both into a single `Channel<RawEvent>`.
- `tracehunter capture --raw` streams `RawEvent`s as JSON to stdout when elevated.
- `WmiActivity` provider is in the enum but excluded from defaults due to the runner-image quirk found in Phase 1. Phase 2 normalizes the 6 providers that are in defaults; WMI normalization can land later.

**Working directory:** `C:\Users\RobertSoligan\OneDrive - tasmonk\Coding_projects\ETW_TraceEvent`
**Branch:** `phase-2-normalization` (already created and pushed)
**Shell:** Bash. **TFM:** `net8.0-windows`. **TreatWarningsAsErrors=true.** **AwesomeAssertions** for tests. **Xunit.SkippableFact** for ETW-dependent tests.

**Conventions:**
- Conventional Commits per task
- TDD: failing test first, then implementation
- No emoji in any file content
- `[SupportedOSPlatform("windows")]` on Windows-only types
- One commit per task

## Critical design decisions for normalization

**1. NormalizedEvent as a discriminated union.** Implement as an abstract `record NormalizedEvent` with `sealed record` subtypes per provider (Process, ImageLoad, Network, Script, Runtime, Dns). All variants share a common envelope (timestamp, pid, ppid, process_image, user_sid, thread_id, host) declared on the abstract base.

**2. Parsers are pure and stateless.** `INormalizedParser` takes a `RawEvent` and returns `NormalizedEvent?` (null when the event is unparseable or filtered). No I/O, no allocation pools — keep parsers easy to unit-test against canned `RawEvent` instances.

**3. Test data via canned RawEvents, not live ETW.** Each parser test constructs a `RawEvent` with a representative `PayloadJson` string (captured from a real ETW trace once and pasted into the test fixture) and asserts the resulting `NormalizedEvent`. This keeps Phase 2 tests fast, deterministic, and runnable in CI without elevation.

**4. Source the payload field names from real ETW traces.** Each provider has slightly different field names in its payload — they come from the provider's manifest. Run `tracehunter capture --raw` (elevated, locally) once to harvest a real example, then write the parser to that schema. Field name guesses without verification have caused issues before; ground in real data.

**5. Two channels in CaptureCoordinator.** Add a normalized channel alongside the existing raw channel. `NormalizationStage` reads from the raw channel, writes to the normalized channel. Both channels are bounded with `DropOldest` semantics. `Reader` (existing) returns the raw reader; new `NormalizedReader` returns the normalized reader.

**6. Backwards compatibility.** Do not modify `UserSessionHost` or `KernelSessionHost` shapes. Do not remove `tracehunter capture --raw`. Add the new normalized output as a new code path so Phase 1 callers keep working.

---

## Task 1: NormalizedEvent type hierarchy

**Files:**
- Create: `src/TraceHunter.Core/NormalizedEvent.cs`
- Test: `tests/TraceHunter.Core.Tests/NormalizedEventTests.cs`

**Step 1: Write the failing tests**

```csharp
using AwesomeAssertions;
using TraceHunter.Core;

namespace TraceHunter.Core.Tests;

public class NormalizedEventTests
{
    [Fact]
    public void Process_event_carries_envelope_and_specific_fields()
    {
        var ev = new NormalizedEvent.Process(
            Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 1234,
            ParentProcessId: 4,
            ThreadId: 5678,
            ProcessImage: "notepad.exe",
            UserSid: "S-1-5-21-1-2-3-1000",
            Host: "WORKSTATION",
            Kind: ProcessEventKind.Start,
            CommandLine: "notepad.exe foo.txt",
            ImagePath: @"C:\Windows\System32\notepad.exe",
            Integrity: "Medium");

        ev.ProviderId.Should().Be(ProviderId.KernelProcess);
        ev.ProcessId.Should().Be(1234);
        ev.CommandLine.Should().Be("notepad.exe foo.txt");
        ev.Kind.Should().Be(ProcessEventKind.Start);
    }

    [Fact]
    public void Network_event_carries_endpoints()
    {
        var ev = new NormalizedEvent.Network(
            Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 1234,
            ParentProcessId: 0,
            ThreadId: 5,
            ProcessImage: "chrome.exe",
            UserSid: null,
            Host: "WORKSTATION",
            Direction: NetworkDirection.Outbound,
            Protocol: "TCP",
            LocalAddress: "192.168.1.10",
            LocalPort: 49152,
            RemoteAddress: "8.8.8.8",
            RemotePort: 443);

        ev.ProviderId.Should().Be(ProviderId.KernelNetwork);
        ev.RemoteAddress.Should().Be("8.8.8.8");
        ev.RemotePort.Should().Be(443);
    }
}
```

**Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~NormalizedEventTests --nologo`
Expected: FAIL (types don't exist).

**Step 3: Implement the type hierarchy**

Create `src/TraceHunter.Core/NormalizedEvent.cs`:

```csharp
namespace TraceHunter.Core;

public abstract record NormalizedEvent(
    DateTimeOffset Timestamp,
    int ProcessId,
    int ParentProcessId,
    int ThreadId,
    string ProcessImage,
    string? UserSid,
    string Host)
{
    public abstract ProviderId ProviderId { get; }

    public sealed record Process(
        DateTimeOffset Timestamp, int ProcessId, int ParentProcessId, int ThreadId,
        string ProcessImage, string? UserSid, string Host,
        ProcessEventKind Kind,
        string CommandLine,
        string ImagePath,
        string? Integrity)
        : NormalizedEvent(Timestamp, ProcessId, ParentProcessId, ThreadId, ProcessImage, UserSid, Host)
    {
        public override ProviderId ProviderId => ProviderId.KernelProcess;
    }

    public sealed record ImageLoad(
        DateTimeOffset Timestamp, int ProcessId, int ParentProcessId, int ThreadId,
        string ProcessImage, string? UserSid, string Host,
        string ImagePath,
        ulong ImageBase,
        int ImageSize)
        : NormalizedEvent(Timestamp, ProcessId, ParentProcessId, ThreadId, ProcessImage, UserSid, Host)
    {
        public override ProviderId ProviderId => ProviderId.KernelImage;
    }

    public sealed record Network(
        DateTimeOffset Timestamp, int ProcessId, int ParentProcessId, int ThreadId,
        string ProcessImage, string? UserSid, string Host,
        NetworkDirection Direction,
        string Protocol,
        string LocalAddress,
        int LocalPort,
        string RemoteAddress,
        int RemotePort)
        : NormalizedEvent(Timestamp, ProcessId, ParentProcessId, ThreadId, ProcessImage, UserSid, Host)
    {
        public override ProviderId ProviderId => ProviderId.KernelNetwork;
    }

    public sealed record Script(
        DateTimeOffset Timestamp, int ProcessId, int ParentProcessId, int ThreadId,
        string ProcessImage, string? UserSid, string Host,
        string ScriptBlockId,
        string Content)
        : NormalizedEvent(Timestamp, ProcessId, ParentProcessId, ThreadId, ProcessImage, UserSid, Host)
    {
        public override ProviderId ProviderId => ProviderId.PowerShell;
    }

    public sealed record Runtime(
        DateTimeOffset Timestamp, int ProcessId, int ParentProcessId, int ThreadId,
        string ProcessImage, string? UserSid, string Host,
        RuntimeEventKind Kind,
        string Detail)
        : NormalizedEvent(Timestamp, ProcessId, ParentProcessId, ThreadId, ProcessImage, UserSid, Host)
    {
        public override ProviderId ProviderId => ProviderId.DotNetRuntime;
    }

    public sealed record Dns(
        DateTimeOffset Timestamp, int ProcessId, int ParentProcessId, int ThreadId,
        string ProcessImage, string? UserSid, string Host,
        DnsEventKind Kind,
        string QueryName,
        string? QueryType,
        string? Result)
        : NormalizedEvent(Timestamp, ProcessId, ParentProcessId, ThreadId, ProcessImage, UserSid, Host)
    {
        public override ProviderId ProviderId => ProviderId.DnsClient;
    }
}

public enum ProcessEventKind { Start, Exit }
public enum NetworkDirection { Inbound, Outbound }
public enum RuntimeEventKind { Exception, Gc, Jit, AssemblyLoad, ThreadPool, Other }
public enum DnsEventKind { Query, Response }
```

**Step 4: Run tests**

Run: `dotnet test --filter FullyQualifiedName~NormalizedEventTests --nologo`
Expected: 2 pass.

**Step 5: Commit**

```bash
git add src/TraceHunter.Core/NormalizedEvent.cs tests/TraceHunter.Core.Tests/NormalizedEventTests.cs
git commit -m "feat(core): add NormalizedEvent discriminated union for 6 providers"
```

---

## Task 2: INormalizedParser interface + ProcessParser (pattern setter)

**Files:**
- Create: `src/TraceHunter.Normalization/INormalizedParser.cs`
- Create: `src/TraceHunter.Normalization/ProcessParser.cs`
- Test: `tests/TraceHunter.Normalization.Tests/ProcessParserTests.cs`

This is the pattern that the next 5 parsers will copy.

**Step 1: Write failing tests**

```csharp
using AwesomeAssertions;
using TraceHunter.Core;
using TraceHunter.Normalization;

namespace TraceHunter.Normalization.Tests;

public class ProcessParserTests
{
    [Fact]
    public void Parse_returns_null_for_non_process_events()
    {
        var parser = new ProcessParser();
        var raw = new RawEvent(
            ProviderId.PowerShell, EventId: 4104, Timestamp: DateTimeOffset.UtcNow,
            ProcessId: 1, ThreadId: 2, PayloadJson: "{}");
        parser.Parse(raw).Should().BeNull();
    }

    [Fact]
    public void Parse_translates_kernel_process_start_payload()
    {
        var parser = new ProcessParser();
        var raw = new RawEvent(
            ProviderId.KernelProcess,
            EventId: 1,
            Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 1234,
            ThreadId: 5,
            PayloadJson: """
                {
                    "imageFileName":"notepad.exe",
                    "commandLine":"notepad.exe foo.txt",
                    "parentId":4
                }
                """);

        var result = parser.Parse(raw);

        result.Should().BeOfType<NormalizedEvent.Process>();
        var p = (NormalizedEvent.Process)result!;
        p.Kind.Should().Be(ProcessEventKind.Start);
        p.CommandLine.Should().Be("notepad.exe foo.txt");
        p.ParentProcessId.Should().Be(4);
        p.ProcessImage.Should().Be("notepad.exe");
    }
}
```

The kernel `KernelSessionHost.OnProcessStart` writes a payload with fields `imageFileName`, `commandLine`, `parentId` (see Phase 1 T4 source). The parser deserializes that.

**Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~ProcessParserTests --nologo`
Expected: FAIL.

**Step 3: Define the interface**

Create `src/TraceHunter.Normalization/INormalizedParser.cs`:

```csharp
using TraceHunter.Core;

namespace TraceHunter.Normalization;

public interface INormalizedParser
{
    bool CanParse(ProviderId providerId);
    NormalizedEvent? Parse(RawEvent raw);
}
```

**Step 4: Implement `ProcessParser`**

Create `src/TraceHunter.Normalization/ProcessParser.cs`:

```csharp
using System.Text.Json;
using TraceHunter.Core;

namespace TraceHunter.Normalization;

public sealed class ProcessParser : INormalizedParser
{
    public bool CanParse(ProviderId providerId) => providerId == ProviderId.KernelProcess;

    public NormalizedEvent? Parse(RawEvent raw)
    {
        if (!CanParse(raw.ProviderId)) return null;

        using var doc = JsonDocument.Parse(raw.PayloadJson);
        var root = doc.RootElement;

        var image = root.TryGetProperty("imageFileName", out var img) ? img.GetString() ?? "" : "";
        var cmdline = root.TryGetProperty("commandLine", out var cmd) ? cmd.GetString() ?? "" : "";
        var parentId = root.TryGetProperty("parentId", out var pp) && pp.TryGetInt32(out var p) ? p : 0;

        // ProcessStart and ProcessStop typically come through as separate event IDs;
        // distinguish by EventID. For Phase 2, treat anything that's not Stop as Start.
        var kind = raw.EventId switch
        {
            2 => ProcessEventKind.Exit,
            _ => ProcessEventKind.Start,
        };

        return new NormalizedEvent.Process(
            Timestamp: raw.Timestamp,
            ProcessId: raw.ProcessId,
            ParentProcessId: parentId,
            ThreadId: raw.ThreadId,
            ProcessImage: image,
            UserSid: null,
            Host: Environment.MachineName,
            Kind: kind,
            CommandLine: cmdline,
            ImagePath: image, // Kernel events often only have the file name; full path comes from enrichment in Phase 3
            Integrity: null);
    }
}
```

**Step 5: Run tests**

Run: `dotnet test --filter FullyQualifiedName~ProcessParserTests --nologo`
Expected: 2 pass.

**Step 6: Commit**

```bash
git add src/TraceHunter.Normalization tests/TraceHunter.Normalization.Tests
git commit -m "feat(normalization): add INormalizedParser and ProcessParser"
```

---

## Task 3: ImageLoadParser

Follow the same pattern as Task 2.

**Files:**
- Create: `src/TraceHunter.Normalization/ImageLoadParser.cs`
- Test: `tests/TraceHunter.Normalization.Tests/ImageLoadParserTests.cs`

The kernel `KernelSessionHost.OnImageLoad` writes payload fields `fileName`, `imageBase` (hex string), `imageSize`. Parser:

```csharp
public sealed class ImageLoadParser : INormalizedParser
{
    public bool CanParse(ProviderId providerId) => providerId == ProviderId.KernelImage;

    public NormalizedEvent? Parse(RawEvent raw)
    {
        if (!CanParse(raw.ProviderId)) return null;

        using var doc = JsonDocument.Parse(raw.PayloadJson);
        var root = doc.RootElement;

        var fileName = root.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
        var imageBaseHex = root.TryGetProperty("imageBase", out var ib) ? ib.GetString() ?? "0" : "0";
        var imageSize = root.TryGetProperty("imageSize", out var sz) && sz.TryGetInt32(out var s) ? s : 0;

        var imageBase = ulong.Parse(imageBaseHex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);

        return new NormalizedEvent.ImageLoad(
            Timestamp: raw.Timestamp, ProcessId: raw.ProcessId, ParentProcessId: 0, ThreadId: raw.ThreadId,
            ProcessImage: "", UserSid: null, Host: Environment.MachineName,
            ImagePath: fileName, ImageBase: imageBase, ImageSize: imageSize);
    }
}
```

Tests: one for non-image events (returns null), one for image load with the canned payload above. Confirm `ImagePath`, `ImageBase` (parsed from hex), `ImageSize` map correctly.

Commit: `feat(normalization): add ImageLoadParser`

---

## Task 4: NetworkParser

The kernel `KernelSessionHost.OnTcpIpConnect` writes payload fields `source` (string `"a.b.c.d:port"`) and `dest` (same format). Parse them by splitting on the last `:`.

**Files:**
- Create: `src/TraceHunter.Normalization/NetworkParser.cs`
- Test: `tests/TraceHunter.Normalization.Tests/NetworkParserTests.cs`

Parser:

```csharp
public sealed class NetworkParser : INormalizedParser
{
    public bool CanParse(ProviderId providerId) => providerId == ProviderId.KernelNetwork;

    public NormalizedEvent? Parse(RawEvent raw)
    {
        if (!CanParse(raw.ProviderId)) return null;

        using var doc = JsonDocument.Parse(raw.PayloadJson);
        var root = doc.RootElement;

        var source = root.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "";
        var dest = root.TryGetProperty("dest", out var dst) ? dst.GetString() ?? "" : "";

        var (la, lp) = ParseEndpoint(source);
        var (ra, rp) = ParseEndpoint(dest);

        return new NormalizedEvent.Network(
            Timestamp: raw.Timestamp, ProcessId: raw.ProcessId, ParentProcessId: 0, ThreadId: raw.ThreadId,
            ProcessImage: "", UserSid: null, Host: Environment.MachineName,
            Direction: NetworkDirection.Outbound, // TcpIpConnect is outbound; Accept is inbound (separate handler)
            Protocol: "TCP",
            LocalAddress: la, LocalPort: lp, RemoteAddress: ra, RemotePort: rp);
    }

    private static (string addr, int port) ParseEndpoint(string s)
    {
        var idx = s.LastIndexOf(':');
        if (idx < 0) return (s, 0);
        var addr = s[..idx];
        var portText = s[(idx + 1)..];
        return (addr, int.TryParse(portText, out var p) ? p : 0);
    }
}
```

Tests: non-network event returns null; canned outbound TCP connect produces correct endpoints.

Commit: `feat(normalization): add NetworkParser`

---

## Task 5: ScriptParser (PowerShell)

The PowerShell ScriptBlock event (event ID 4104) has payload fields including `MessageNumber`, `MessageTotal`, `ScriptBlockId`, `ScriptBlockText`. The `UserSessionHost`'s generic `SerializePayload` captures all fields. Parser extracts the script block id and text.

**Files:**
- Create: `src/TraceHunter.Normalization/ScriptParser.cs`
- Test: `tests/TraceHunter.Normalization.Tests/ScriptParserTests.cs`

Parser:

```csharp
public sealed class ScriptParser : INormalizedParser
{
    public bool CanParse(ProviderId providerId) => providerId == ProviderId.PowerShell;

    public NormalizedEvent? Parse(RawEvent raw)
    {
        if (!CanParse(raw.ProviderId)) return null;
        if (raw.EventId != 4104) return null;  // ScriptBlockLogging only

        using var doc = JsonDocument.Parse(raw.PayloadJson);
        var root = doc.RootElement;

        var scriptBlockId = root.TryGetProperty("ScriptBlockId", out var sb) ? sb.GetString() ?? "" : "";
        var content = root.TryGetProperty("ScriptBlockText", out var st) ? st.GetString() ?? "" : "";

        return new NormalizedEvent.Script(
            Timestamp: raw.Timestamp, ProcessId: raw.ProcessId, ParentProcessId: 0, ThreadId: raw.ThreadId,
            ProcessImage: "", UserSid: null, Host: Environment.MachineName,
            ScriptBlockId: scriptBlockId, Content: content);
    }
}
```

Tests: non-PS event returns null; non-4104 event returns null; canned 4104 event parses correctly.

Commit: `feat(normalization): add ScriptParser for PowerShell ScriptBlockLogging`

---

## Task 6: RuntimeParser (CLR)

The .NET runtime provider emits many event types — exceptions (event ID 1), GC (multiple), JIT, assembly load, thread pool. For Phase 2, normalize the most-useful subset and bucket the rest as `Other`.

**Files:**
- Create: `src/TraceHunter.Normalization/RuntimeParser.cs`
- Test: `tests/TraceHunter.Normalization.Tests/RuntimeParserTests.cs`

Parser maps event IDs to `RuntimeEventKind`:
- ID 1 (Exception): `Exception`, detail = `ExceptionType + ": " + ExceptionMessage`
- ID 2-7 (GC): `Gc`, detail summarizes generation/reason
- ID 145 (MethodJittingStarted) etc: `Jit`, detail = MethodNamespace.MethodName
- ID 154 (AssemblyLoad): `AssemblyLoad`, detail = AssemblyName
- ID 50-60 (ThreadPool): `ThreadPool`, detail summarizes
- Anything else: `Other`, detail = `$"EventId={raw.EventId}"`

For Phase 2, only Exception, AssemblyLoad, and Other are required to be handled with detail; the rest can use a stub detail string.

Tests: non-CLR event returns null; canned exception event with payload `{"ExceptionType":"...","ExceptionMessage":"..."}` parses correctly.

Commit: `feat(normalization): add RuntimeParser for CLR events`

---

## Task 7: DnsParser

The DNS Client provider emits Query (3006) and Response (3008) events. Payload includes `QueryName`, `QueryType`, `QueryResults`.

**Files:**
- Create: `src/TraceHunter.Normalization/DnsParser.cs`
- Test: `tests/TraceHunter.Normalization.Tests/DnsParserTests.cs`

Parser maps event IDs to `DnsEventKind`:
- 3006 -> Query
- 3008 -> Response
- Anything else returns null

Tests: non-DNS event returns null; canned Query and Response events parse correctly.

Commit: `feat(normalization): add DnsParser`

---

## Task 8: NormalizationStage (orchestrator) + ParserRegistry

**Files:**
- Create: `src/TraceHunter.Normalization/ParserRegistry.cs`
- Create: `src/TraceHunter.Normalization/NormalizationStage.cs`
- Test: `tests/TraceHunter.Normalization.Tests/NormalizationStageTests.cs`

`ParserRegistry` is a thin lookup keyed by `ProviderId` returning the right parser.

`NormalizationStage` reads from a `ChannelReader<RawEvent>`, dispatches each event to the registry, writes successful parses to a `ChannelWriter<NormalizedEvent>`. Runs the dispatch loop on `Task.Run` until cancellation.

```csharp
public sealed class ParserRegistry
{
    private readonly Dictionary<ProviderId, INormalizedParser> _parsers;

    public ParserRegistry(IEnumerable<INormalizedParser> parsers)
    {
        _parsers = new();
        foreach (var p in parsers)
        {
            foreach (ProviderId id in Enum.GetValues<ProviderId>())
            {
                if (p.CanParse(id))
                    _parsers[id] = p;
            }
        }
    }

    public INormalizedParser? Resolve(ProviderId id) => _parsers.GetValueOrDefault(id);
}

public sealed class NormalizationStage : IAsyncDisposable
{
    private readonly ParserRegistry _registry;
    private readonly ChannelReader<RawEvent> _input;
    private readonly ChannelWriter<NormalizedEvent> _output;
    private Task? _loop;
    private CancellationTokenSource? _cts;

    public NormalizationStage(ParserRegistry registry, ChannelReader<RawEvent> input, ChannelWriter<NormalizedEvent> output)
    {
        _registry = registry;
        _input = input;
        _output = output;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => RunLoop(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task RunLoop(CancellationToken ct)
    {
        try
        {
            await foreach (var raw in _input.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var parser = _registry.Resolve(raw.ProviderId);
                if (parser is null) continue;
                var normalized = parser.Parse(raw);
                if (normalized is null) continue;
                _output.TryWrite(normalized);
            }
        }
        catch (OperationCanceledException) { /* clean shutdown */ }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException) { }
        }
        _cts?.Dispose();
        _output.TryComplete();
    }
}
```

Tests: registry resolves correct parser per ProviderId; stage transforms a queue of RawEvents into NormalizedEvents end-to-end; stage stops cleanly on cancellation.

Commit: `feat(normalization): add ParserRegistry and NormalizationStage`

---

## Task 9: Wire NormalizationStage into CaptureCoordinator

**Files:**
- Modify: `src/TraceHunter.Capture/CaptureCoordinator.cs`
- Test: `tests/TraceHunter.Capture.Tests/CaptureCoordinatorTests.cs` (add a new test)

Behavior: `CaptureCoordinator` constructs a second bounded channel for `NormalizedEvent`, constructs a `ParserRegistry` (with all 6 parsers), constructs a `NormalizationStage` connecting the raw reader to the normalized writer, and exposes a new `NormalizedReader` property.

The existing `Reader` (raw) keeps working for `--raw` callers. New normalized callers use `NormalizedReader`.

The Coordinator's StartAsync must start the normalization stage AFTER the session hosts (so it has a populated raw channel to read from). DisposeAsync must dispose the stage BEFORE the session hosts (so the stage stops draining first).

Note: `TraceHunter.Capture.csproj` will need a project reference to `TraceHunter.Normalization`. Add it.

Add a test (no elevation needed) that verifies `NormalizedReader` is non-null after construction. The end-to-end "normalized event arrives" assertion lands in the integration test in T11.

Commit: `feat(capture): wire NormalizationStage into CaptureCoordinator`

---

## Task 10: Add `tracehunter capture` CLI verb (no flag = normalized)

**Files:**
- Modify: `src/TraceHunter.Host/Commands/CaptureCommand.cs`

Behavior: when `--raw` is NOT supplied (the default), stream `NormalizedEvent` JSON instead of `RawEvent` JSON. Use `JsonSerializer.Serialize(coordinator.NormalizedReader.ReadAllAsync(...))`.

Make sure `JsonSerializer` is configured to serialize the discriminated union sensibly — by default, polymorphic serialization in `System.Text.Json` requires `[JsonDerivedType]` attributes on the base. Add them to `NormalizedEvent`:

```csharp
[JsonDerivedType(typeof(Process), nameof(Process))]
[JsonDerivedType(typeof(ImageLoad), nameof(ImageLoad))]
[JsonDerivedType(typeof(Network), nameof(Network))]
[JsonDerivedType(typeof(Script), nameof(Script))]
[JsonDerivedType(typeof(Runtime), nameof(Runtime))]
[JsonDerivedType(typeof(Dns), nameof(Dns))]
public abstract record NormalizedEvent(...) { ... }
```

This makes serialized JSON include a `$type` discriminator field. Each event line on stdout looks like:

```json
{"$type":"Process","Timestamp":"2026-04-22T...","ProcessId":1234,...}
```

Update CLI:

```csharp
cmd.SetAction(async (parseResult, ct) =>
{
    var raw = parseResult.GetValue(rawOption);
    var settings = new CaptureSettings();
    await using var coordinator = new CaptureCoordinator(settings, new PrivilegeProbe());
    await coordinator.StartAsync(ct);

    var status = coordinator.GetStatus();
    if (status.NeedsElevation)
    {
        // ...existing elevation guidance, exit 3
    }

    Console.Error.WriteLine($"Capturing... press Ctrl+C to stop. Status: {JsonSerializer.Serialize(status)}");

    if (raw)
    {
        await foreach (var ev in coordinator.Reader.ReadAllAsync(ct))
        {
            Console.WriteLine(JsonSerializer.Serialize(ev));
        }
    }
    else
    {
        await foreach (var ev in coordinator.NormalizedReader.ReadAllAsync(ct))
        {
            Console.WriteLine(JsonSerializer.Serialize<NormalizedEvent>(ev));
        }
    }

    return 0;
});
```

Also remove the previous "without --raw is not yet implemented" stub.

No new tests for the CLI (manual smoke test is the verification path). Existing CLI behavior preserved.

Commit: `feat(host): emit normalized events by default; --raw still works`

---

## Task 11: Final verification + push + open PR

**Step 1: Clean build**

```bash
dotnet clean
dotnet build --configuration Release --nologo
```
Expected: 0 warnings, 0 errors, 16 projects.

**Step 2: All tests**

```bash
dotnet test --configuration Release --no-build --nologo
```
Expected:
- Phase 1 totals (16 passed, 4 skipped) carry forward.
- Phase 2 adds: 2 NormalizedEvent tests, 12+ parser tests (2 per parser x 6 parsers), 2-3 NormalizationStage tests, 1 CaptureCoordinator test.
- Total expected: ~32 passed, 4 skipped, 0 failed.

**Step 3: Format check**

```bash
dotnet format --verify-no-changes --no-restore
```

**Step 4: Optional smoke test (USER STEP)**

User runs from elevated PowerShell:

```bash
dotnet run --project src/TraceHunter.Host --configuration Release -- capture
```

Expected: stderr shows status JSON; stdout streams typed `NormalizedEvent` JSON lines like `{"$type":"Runtime","Kind":"Exception",...}`.

**Step 5: Push**

```bash
git push origin phase-2-normalization
```

**Step 6: Open PR**

```bash
gh pr create --base main --head phase-2-normalization --title "Phase 2: Normalization" --body "..."
```

PR body summarizes:
- Per-provider parsers for Process, ImageLoad, Network, Script (PowerShell), Runtime (CLR), Dns
- New NormalizationStage that pipes raw -> normalized
- CaptureCoordinator now exposes both raw and normalized channels
- CLI default `tracehunter capture` emits normalized JSON; `--raw` still works
- ~16 new tests
- Phase 3 (Enrichment + provenance graph) is next

**Step 7: Watch CI**

```bash
gh pr checks <PR#> --repo 3ch0p01nt/tracehunter --watch --fail-fast
```

If green, ready to merge.

---

## Phase 2 - Done criteria

- 11 tasks committed atomically with Conventional Commits
- `dotnet build` clean, `dotnet test` passes (~32 passed, 4 skipped, 0 failed)
- `tracehunter capture` (no flag) emits typed NormalizedEvent JSON
- `tracehunter capture --raw` still emits RawEvent JSON
- CI green on PR

After this phase, Phase 3 (Enrichment + Provenance Graph) is the next plan: process tree maintenance, Authenticode signing cache, DNS<->Network correlation, snapshot API for the Web UI to consume.
