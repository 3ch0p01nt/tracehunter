# TraceHunter Phase 1 - Capture Foundation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Stand up the ETW capture layer end-to-end. By the end of this phase, `tracehunter capture --raw` reads real ETW events from kernel + user-mode providers, normalizes them into a `RawEvent` envelope, and prints newline-delimited JSON to stdout. Provides the foundation for Phase 2 (full normalization) and beyond.

**Architecture:** Two `TraceEventSession` hosts (kernel + user), one for high-privilege providers and one for unprivileged ones. Each runs the TraceEvent dispatch loop on a dedicated thread. Both feed a single `Channel<RawEvent>` consumed by downstream stages. A `CaptureCoordinator` orchestrates the hosts, surfaces health/status, and applies graceful privilege fallback (drops kernel providers when not admin, keeps the rest running). Disposal is bombproof - real ETW sessions persist across process death if leaked, so robust cleanup is non-negotiable.

**Tech Stack:** .NET 8, `Microsoft.Diagnostics.Tracing.TraceEvent` 3.2.2, `System.Threading.Channels`, `System.CommandLine`, `Microsoft.Extensions.Hosting`, xUnit + AwesomeAssertions for tests, custom `EventSource` for synthetic test events.

---

## Context for the executor

You're implementing Phase 1 of TraceHunter. Read these before starting:
- `docs/plans/2026-04-22-tracehunter-design.md` sections 4.1 (Capture layer), 5 (Data flow), 7.2 (Provider failure isolation), 7.3 (Privilege degradation).
- `docs/plans/2026-04-22-phase-0-bootstrap.md` for what's already built.
- `samples/README.md` to know where sample rules and scripts will live in later phases.

Phase 0 left you with a clean scaffold: 8 src/ projects, 8 test projects, AwesomeAssertions for assertions, central NuGet versions in `Directory.Packages.props`, `TreatWarningsAsErrors=true`, `net8.0-windows` target framework. Tests have analyzer relaxation (CA1707/CA1034 muted) so test method names like `StartAsync_when_admin_starts_kernel_session()` are fine.

**Working directory:** `C:\Users\RobertSoligan\OneDrive - tasmonk\Coding_projects\ETW_TraceEvent`
**Branch:** `phase-1-capture`
**Shell:** Bash (Git for Windows). Forward slashes in paths; quote anything with spaces.
**Commits:** One commit per task. Conventional Commits (`feat:`, `test:`, `refactor:`).

## Critical TraceEvent API knowledge

**`TraceEventSession`** is the core type. For real-time consumption:

```csharp
using var session = new TraceEventSession("TraceHunter-User");
session.EnableProvider(MyEventSource.ProviderGuid);
session.Source.Dynamic.All += data => { /* handle */ };
session.Source.Process(); // BLOCKS until Stop() called from another thread
```

For kernel events:

```csharp
using var session = new TraceEventSession(KernelTraceEventParser.KernelSessionName);
session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process | KernelTraceEventParser.Keywords.ImageLoad);
session.Source.Kernel.ProcessStart += data => { /* handle */ };
session.Source.Process();
```

**Hard rules:**
- `session.Source.Process()` is blocking - run it on a dedicated thread (`Task.Run` is acceptable but kill it cleanly on shutdown).
- `session.Stop()` (from another thread) ends `Process()`. Always call before disposal.
- Each kernel session needs the literal name `KernelTraceEventParser.KernelSessionName` (typically `"NT Kernel Logger"`); only one such session can exist per machine. If a previous TraceHunter session leaked, the new one's start will fail - detect and clean up via `TraceEventSession.GetActiveSessionNames()` + force-stop.
- Kernel session requires admin (`SeDebugPrivilege` + `SeSystemProfilePrivilege`). User-mode provider sessions do not.
- TraceEvent throws if you call `EnableProvider` with a guid that has no registered manifest on the machine - wrap in try/catch and treat as a per-provider failure, not session-fatal.
- When a session is disposed, the OS removes the kernel-side state. If the process crashes before disposal, the session lingers. Always handle `Console.CancelKeyPress` (Ctrl-C) and `AppDomain.ProcessExit` for best-effort cleanup.

**Sample reference:** the official sample at https://github.com/microsoft/perfview/blob/main/src/TraceEvent/Samples/30_TraceEventAPI.cs walks through these patterns. Read it before writing any session code.

## Test strategy for ETW

**Custom `EventSource` for unit/integration tests.** ETW lets you register a managed `EventSource`-derived class with a known GUID; you can then start a TraceEventSession subscribed to that GUID, emit events from your test, and verify they flow through the pipeline. This avoids dependence on real OS providers in CI.

Pattern:

```csharp
[EventSource(Name = "TraceHunter-Test")]
internal sealed class TestEventSource : EventSource
{
    public static readonly TestEventSource Log = new();
    public static readonly Guid ProviderGuid = new("d68f3eaf-39ad-5c66-...");

    [Event(1)]
    public void Ping(string message) => WriteEvent(1, message);
}
```

Use `EventSource.GetGuid(typeof(TestEventSource))` to compute the GUID from the name (`TraceHunter-Test` -> deterministic GUID via `EventSource.GetGuid` algorithm).

**Skip kernel-session tests on non-admin runs.** Use xUnit `[SkippableFact]` (from `Xunit.SkippableFact` package) or a custom `[FactAdminOnly]` attribute that probes for elevation and skips otherwise. CI runners (GitHub Actions `windows-latest`) do NOT run tests as admin, so kernel session tests must skip there.

**Session naming in tests.** Use a unique per-test session name (e.g. `$"TH-Test-{Guid.NewGuid():N}"`) so concurrent test runs don't collide. Always wrap in `using` so the session is disposed even on failure.

## NuGet package additions

Add these to `Directory.Packages.props` (you'll do this in Task 1):
- `Xunit.SkippableFact` (latest stable) - lets specific tests skip when elevation isn't available

Already present: `Microsoft.Diagnostics.Tracing.TraceEvent`, `System.CommandLine`, `Microsoft.Extensions.Hosting`, AwesomeAssertions, xUnit.

---

## Task 1: Core types + skippable test infrastructure

**Files:**
- Create: `src/TraceHunter.Core/RawEvent.cs`
- Create: `src/TraceHunter.Core/ProviderId.cs`
- Create: `src/TraceHunter.Core/CaptureSettings.cs`
- Create: `src/TraceHunter.Core/CaptureStatus.cs`
- Modify: `Directory.Packages.props` (add `Xunit.SkippableFact`)
- Modify: `tests/TraceHunter.Capture.Tests/TraceHunter.Capture.Tests.csproj` (add `Xunit.SkippableFact` reference)
- Modify: `tests/TraceHunter.Integration.Tests/TraceHunter.Integration.Tests.csproj` (add `Xunit.SkippableFact` reference)
- Test: `tests/TraceHunter.Core.Tests/RawEventTests.cs`

**Step 1: Write the failing tests for `RawEvent`**

```csharp
namespace TraceHunter.Core.Tests;

public class RawEventTests
{
    [Fact]
    public void Constructor_sets_all_fields()
    {
        var ts = DateTimeOffset.UtcNow;
        var ev = new RawEvent(
            ProviderId: ProviderId.KernelProcess,
            EventId: 1,
            Timestamp: ts,
            ProcessId: 1234,
            ThreadId: 5678,
            PayloadJson: """{"image":"notepad.exe"}""");

        ev.ProviderId.Should().Be(ProviderId.KernelProcess);
        ev.EventId.Should().Be(1);
        ev.Timestamp.Should().Be(ts);
        ev.ProcessId.Should().Be(1234);
        ev.ThreadId.Should().Be(5678);
        ev.PayloadJson.Should().Be("""{"image":"notepad.exe"}""");
    }

    [Fact]
    public void Constructor_rejects_negative_pid()
    {
        var act = () => new RawEvent(
            ProviderId.KernelProcess, EventId: 1, Timestamp: DateTimeOffset.UtcNow,
            ProcessId: -1, ThreadId: 0, PayloadJson: "{}");

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ProcessId");
    }

    [Fact]
    public void Constructor_rejects_null_payload()
    {
        var act = () => new RawEvent(
            ProviderId.KernelProcess, EventId: 1, Timestamp: DateTimeOffset.UtcNow,
            ProcessId: 0, ThreadId: 0, PayloadJson: null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~RawEventTests --nologo`
Expected: FAIL (compile error - types don't exist).

**Step 3: Implement `ProviderId` enum**

Create `src/TraceHunter.Core/ProviderId.cs`:

```csharp
namespace TraceHunter.Core;

public enum ProviderId
{
    Unknown = 0,
    KernelProcess,
    KernelImage,
    KernelNetwork,
    PowerShell,
    DotNetRuntime,
    DnsClient,
    WmiActivity,
}
```

**Step 4: Implement `RawEvent` record**

Create `src/TraceHunter.Core/RawEvent.cs`:

```csharp
namespace TraceHunter.Core;

public sealed record RawEvent(
    ProviderId ProviderId,
    int EventId,
    DateTimeOffset Timestamp,
    int ProcessId,
    int ThreadId,
    string PayloadJson)
{
    public ProviderId ProviderId { get; } = ProviderId;
    public int ProcessId { get; } = ProcessId >= 0
        ? ProcessId
        : throw new ArgumentOutOfRangeException(nameof(ProcessId), "Process ID must be non-negative.");
    public string PayloadJson { get; } = PayloadJson ?? throw new ArgumentNullException(nameof(PayloadJson));
}
```

**Step 5: Implement `CaptureSettings`**

Create `src/TraceHunter.Core/CaptureSettings.cs`:

```csharp
namespace TraceHunter.Core;

public sealed record CaptureSettings
{
    public bool EnableKernelSession { get; init; } = true;
    public bool EnableUserSession { get; init; } = true;
    public IReadOnlySet<ProviderId> EnabledProviders { get; init; } = AllProviders;
    public int ChannelCapacity { get; init; } = 100_000;
    public string KernelSessionName { get; init; } = "TraceHunter-Kernel";
    public string UserSessionName { get; init; } = "TraceHunter-User";

    public static IReadOnlySet<ProviderId> AllProviders { get; } = new HashSet<ProviderId>
    {
        ProviderId.KernelProcess, ProviderId.KernelImage, ProviderId.KernelNetwork,
        ProviderId.PowerShell, ProviderId.DotNetRuntime, ProviderId.DnsClient, ProviderId.WmiActivity,
    };
}
```

Note: `KernelSessionName` is a hint; the actual kernel session may need the literal `KernelTraceEventParser.KernelSessionName` name from TraceEvent. The host implementation in T4 will reconcile.

**Step 6: Implement `CaptureStatus`**

Create `src/TraceHunter.Core/CaptureStatus.cs`:

```csharp
namespace TraceHunter.Core;

public sealed record CaptureStatus(
    IReadOnlyDictionary<ProviderId, ProviderState> ProviderStates,
    long EventsObserved,
    long EventsDropped);

public enum ProviderState
{
    NotConfigured,
    Starting,
    Running,
    Failed,
    Stopped,
}
```

**Step 7: Add `Xunit.SkippableFact` to Directory.Packages.props**

Edit `Directory.Packages.props`, add inside `<ItemGroup>`:

```xml
<PackageVersion Include="Xunit.SkippableFact" Version="1.5.23" />
```

(Use latest stable version as shown by `dotnet list package --outdated`.)

**Step 8: Reference Xunit.SkippableFact in capture and integration tests**

In `tests/TraceHunter.Capture.Tests/TraceHunter.Capture.Tests.csproj` and `tests/TraceHunter.Integration.Tests/TraceHunter.Integration.Tests.csproj`, add inside the test `<ItemGroup>`:

```xml
<PackageReference Include="Xunit.SkippableFact" />
```

**Step 9: Run all tests**

Run: `dotnet test --nologo`
Expected: 11 passing (3 new RawEvent tests + 8 existing smoke tests). 0 failing.

**Step 10: Commit**

```bash
git add src/TraceHunter.Core Directory.Packages.props tests/TraceHunter.Capture.Tests tests/TraceHunter.Integration.Tests tests/TraceHunter.Core.Tests
git commit -m "feat(core): add RawEvent, ProviderId, CaptureSettings, CaptureStatus"
```

---

## Task 2: Privilege probe with TDD

**Files:**
- Create: `src/TraceHunter.Capture/IPrivilegeProbe.cs`
- Create: `src/TraceHunter.Capture/PrivilegeProbe.cs`
- Test: `tests/TraceHunter.Capture.Tests/PrivilegeProbeTests.cs`

**Step 1: Write the failing tests**

```csharp
using AwesomeAssertions;
using TraceHunter.Capture;

namespace TraceHunter.Capture.Tests;

public class PrivilegeProbeTests
{
    [Fact]
    public void IsElevated_returns_a_boolean_without_throwing()
    {
        var probe = new PrivilegeProbe();
        // We don't assert true/false because the test runs in unknown elevation.
        // We just assert the call completes and returns a boolean.
        var act = () => probe.IsElevated();
        act.Should().NotThrow();
    }

    [Fact]
    public void IsElevated_returns_consistent_value_on_repeat_calls()
    {
        var probe = new PrivilegeProbe();
        var first = probe.IsElevated();
        var second = probe.IsElevated();
        second.Should().Be(first);
    }
}
```

**Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~PrivilegeProbeTests --nologo`
Expected: FAIL (types don't exist).

**Step 3: Define interface**

Create `src/TraceHunter.Capture/IPrivilegeProbe.cs`:

```csharp
namespace TraceHunter.Capture;

public interface IPrivilegeProbe
{
    bool IsElevated();
}
```

**Step 4: Implement**

Create `src/TraceHunter.Capture/PrivilegeProbe.cs`:

```csharp
using System.Security.Principal;
using System.Runtime.Versioning;

namespace TraceHunter.Capture;

[SupportedOSPlatform("windows")]
public sealed class PrivilegeProbe : IPrivilegeProbe
{
    public bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
```

**Step 5: Run tests**

Run: `dotnet test --filter FullyQualifiedName~PrivilegeProbeTests --nologo`
Expected: 2 pass.

**Step 6: Commit**

```bash
git add src/TraceHunter.Capture tests/TraceHunter.Capture.Tests
git commit -m "feat(capture): add PrivilegeProbe"
```

---

## Task 3: ISessionHost interface + UserSessionHost (TDD with synthetic EventSource)

**Files:**
- Create: `src/TraceHunter.Capture/ISessionHost.cs`
- Create: `src/TraceHunter.Capture/UserSessionHost.cs`
- Create: `tests/TraceHunter.Capture.Tests/TestEventSource.cs`
- Test: `tests/TraceHunter.Capture.Tests/UserSessionHostTests.cs`

**Step 1: Write the failing test**

```csharp
using System.Diagnostics.Tracing;
using System.Threading.Channels;
using AwesomeAssertions;
using TraceHunter.Capture;
using TraceHunter.Core;
using Xunit;

namespace TraceHunter.Capture.Tests;

[EventSource(Name = "TraceHunter-Test-Capture")]
internal sealed class TestEventSource : EventSource
{
    public static readonly TestEventSource Log = new();
    public static readonly Guid ProviderGuid = EventSource.GetGuid(typeof(TestEventSource));

    [Event(1, Level = EventLevel.Informational)]
    public void Ping(string message) => WriteEvent(1, message);
}

public class UserSessionHostTests
{
    [SkippableFact]
    public async Task StartAsync_then_Ping_event_arrives_in_channel()
    {
        // Skip if not Windows (TraceEvent only works on Windows)
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

        var sessionName = $"TH-Test-{Guid.NewGuid():N}";
        var channel = Channel.CreateBounded<RawEvent>(100);

        await using var host = new UserSessionHost(
            sessionName,
            new[] { (TestEventSource.ProviderGuid, ProviderId.Unknown) },
            channel.Writer);

        await host.StartAsync(CancellationToken.None);

        // Give the dispatch loop a moment to attach
        await Task.Delay(500);

        TestEventSource.Log.Ping("hello");

        // Read with timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await channel.Reader.ReadAsync(cts.Token);

        received.ProviderId.Should().Be(ProviderId.Unknown);
        received.EventId.Should().Be(1);
        received.PayloadJson.Should().Contain("hello");
    }
}
```

The test uses `[SkippableFact]` so it can run on Linux CI containers (which would skip) but execute on Windows.

**Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~UserSessionHostTests --nologo`
Expected: FAIL (UserSessionHost doesn't exist).

**Step 3: Define interface**

Create `src/TraceHunter.Capture/ISessionHost.cs`:

```csharp
namespace TraceHunter.Capture;

public interface ISessionHost : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

**Step 4: Implement `UserSessionHost`**

Create `src/TraceHunter.Capture/UserSessionHost.cs`:

```csharp
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using TraceHunter.Core;

namespace TraceHunter.Capture;

[SupportedOSPlatform("windows")]
public sealed class UserSessionHost : ISessionHost
{
    private readonly string _sessionName;
    private readonly IReadOnlyCollection<(Guid Provider, ProviderId Id)> _providers;
    private readonly ChannelWriter<RawEvent> _writer;
    private TraceEventSession? _session;
    private Task? _dispatchTask;

    public UserSessionHost(
        string sessionName,
        IEnumerable<(Guid Provider, ProviderId Id)> providers,
        ChannelWriter<RawEvent> writer)
    {
        _sessionName = sessionName;
        _providers = providers.ToList();
        _writer = writer;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Clean up any leaked session with the same name
        if (TraceEventSession.GetActiveSessionNames().Contains(_sessionName))
        {
            using var leaked = new TraceEventSession(_sessionName) { StopOnDispose = true };
            leaked.Stop();
        }

        _session = new TraceEventSession(_sessionName);
        var providerLookup = _providers.ToDictionary(p => p.Provider, p => p.Id);

        _session.Source.Dynamic.All += data =>
        {
            if (!providerLookup.TryGetValue(data.ProviderGuid, out var providerId))
                return;

            var payload = SerializePayload(data);
            var ev = new RawEvent(
                ProviderId: providerId,
                EventId: (int)data.ID,
                Timestamp: data.TimeStamp,
                ProcessId: data.ProcessID >= 0 ? data.ProcessID : 0,
                ThreadId: data.ThreadID >= 0 ? data.ThreadID : 0,
                PayloadJson: payload);

            _writer.TryWrite(ev);
        };

        foreach (var (providerGuid, _) in _providers)
        {
            try { _session.EnableProvider(providerGuid); }
            catch { /* per-provider failure tolerated; provider will simply not emit */ }
        }

        _dispatchTask = Task.Run(() => _session.Source.Process(), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _session?.Stop();
        if (_dispatchTask is not null)
        {
            try { await _dispatchTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
            catch (TimeoutException) { /* dispatch loop exit best-effort */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _session?.Dispose();
    }

    private static string SerializePayload(TraceEvent data)
    {
        var dict = new Dictionary<string, object?>(data.PayloadNames.Length);
        for (int i = 0; i < data.PayloadNames.Length; i++)
        {
            dict[data.PayloadNames[i]] = data.PayloadValue(i);
        }
        return JsonSerializer.Serialize(dict);
    }
}
```

**Step 5: Run tests**

Run: `dotnet test --filter FullyQualifiedName~UserSessionHostTests --nologo`
Expected: PASS on Windows (test asserts the Ping event lands in the channel).

If you're not running as admin, the user-mode session does NOT need elevation. The test should pass without UAC.

If the test times out: increase the post-Start delay; ETW dispatch loop attachment can be slow on first-run with cold caches.

**Step 6: Commit**

```bash
git add src/TraceHunter.Capture tests/TraceHunter.Capture.Tests
git commit -m "feat(capture): add UserSessionHost with synthetic EventSource integration test"
```

---

## Task 4: KernelSessionHost (skippable on non-admin)

**Files:**
- Create: `src/TraceHunter.Capture/KernelSessionHost.cs`
- Test: `tests/TraceHunter.Capture.Tests/KernelSessionHostTests.cs`

**Step 1: Write the failing test (admin-only)**

```csharp
using System.Threading.Channels;
using AwesomeAssertions;
using TraceHunter.Capture;
using TraceHunter.Core;
using Xunit;

namespace TraceHunter.Capture.Tests;

public class KernelSessionHostTests
{
    [SkippableFact]
    public async Task StartAsync_when_admin_emits_process_events()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        Skip.IfNot(new PrivilegeProbe().IsElevated(), "Requires elevated process");

        var channel = Channel.CreateBounded<RawEvent>(1000);
        await using var host = new KernelSessionHost(channel.Writer);

        await host.StartAsync(CancellationToken.None);

        // Spawn a child process to generate a ProcessStart event
        await Task.Delay(500);
        using var p = System.Diagnostics.Process.Start("cmd.exe", "/c exit");
        p.WaitForExit();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        RawEvent? received = null;
        await foreach (var ev in channel.Reader.ReadAllAsync(cts.Token))
        {
            if (ev.ProviderId == ProviderId.KernelProcess)
            {
                received = ev;
                break;
            }
        }

        received.Should().NotBeNull();
        received!.ProviderId.Should().Be(ProviderId.KernelProcess);
    }
}
```

**Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~KernelSessionHostTests --nologo`
Expected: FAIL or SKIP (KernelSessionHost doesn't exist; if not admin, skip).

**Step 3: Implement `KernelSessionHost`**

Create `src/TraceHunter.Capture/KernelSessionHost.cs`:

```csharp
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using TraceHunter.Core;

namespace TraceHunter.Capture;

[SupportedOSPlatform("windows")]
public sealed class KernelSessionHost : ISessionHost
{
    private readonly ChannelWriter<RawEvent> _writer;
    private TraceEventSession? _session;
    private Task? _dispatchTask;

    public KernelSessionHost(ChannelWriter<RawEvent> writer)
    {
        _writer = writer;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var sessionName = KernelTraceEventParser.KernelSessionName;

        if (TraceEventSession.GetActiveSessionNames().Contains(sessionName))
        {
            using var leaked = new TraceEventSession(sessionName) { StopOnDispose = true };
            leaked.Stop();
        }

        _session = new TraceEventSession(sessionName);
        _session.EnableKernelProvider(
            KernelTraceEventParser.Keywords.Process |
            KernelTraceEventParser.Keywords.ImageLoad |
            KernelTraceEventParser.Keywords.NetworkTCPIP);

        _session.Source.Kernel.ProcessStart += data =>
        {
            EmitRaw(ProviderId.KernelProcess, (int)data.ID, data.TimeStamp, data.ProcessID, data.ThreadID, new
            {
                imageFileName = data.ImageFileName,
                commandLine = data.CommandLine,
                parentId = data.ParentID,
            });
        };

        _session.Source.Kernel.ImageLoad += data =>
        {
            EmitRaw(ProviderId.KernelImage, (int)data.ID, data.TimeStamp, data.ProcessID, data.ThreadID, new
            {
                fileName = data.FileName,
                imageBase = data.ImageBase.ToString("X"),
                imageSize = data.ImageSize,
            });
        };

        _session.Source.Kernel.TcpIpConnect += data =>
        {
            EmitRaw(ProviderId.KernelNetwork, (int)data.ID, data.TimeStamp, data.ProcessID, data.ThreadID, new
            {
                source = $"{data.saddr}:{data.sport}",
                dest = $"{data.daddr}:{data.dport}",
            });
        };

        _dispatchTask = Task.Run(() => _session.Source.Process(), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _session?.Stop();
        if (_dispatchTask is not null)
        {
            try { await _dispatchTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
            catch (TimeoutException) { /* best-effort */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _session?.Dispose();
    }

    private void EmitRaw(ProviderId provider, int eventId, DateTime timestamp, int pid, int tid, object payload)
    {
        var ev = new RawEvent(
            ProviderId: provider,
            EventId: eventId,
            Timestamp: new DateTimeOffset(timestamp, TimeSpan.Zero),
            ProcessId: pid >= 0 ? pid : 0,
            ThreadId: tid >= 0 ? tid : 0,
            PayloadJson: JsonSerializer.Serialize(payload));
        _writer.TryWrite(ev);
    }
}
```

**Step 4: Run tests**

Run: `dotnet test --filter FullyQualifiedName~KernelSessionHostTests --nologo`
- If running as admin: expect PASS.
- If not admin: expect SKIP (with message "Requires elevated process").
- Either outcome is acceptable; CI will skip.

To run the test with admin: open an elevated terminal and re-run from there.

**Step 5: Commit**

```bash
git add src/TraceHunter.Capture tests/TraceHunter.Capture.Tests
git commit -m "feat(capture): add KernelSessionHost for Process/Image/TcpIp providers"
```

---

## Task 5: CaptureCoordinator with privilege fallback

**Files:**
- Create: `src/TraceHunter.Capture/CaptureCoordinator.cs`
- Test: `tests/TraceHunter.Capture.Tests/CaptureCoordinatorTests.cs`

**Step 1: Write the failing tests**

```csharp
using System.Threading.Channels;
using AwesomeAssertions;
using TraceHunter.Capture;
using TraceHunter.Core;
using Xunit;

namespace TraceHunter.Capture.Tests;

public class CaptureCoordinatorTests
{
    private sealed class FakePrivilegeProbe(bool elevated) : IPrivilegeProbe
    {
        public bool IsElevated() => elevated;
    }

    [SkippableFact]
    public async Task StartAsync_when_not_admin_skips_kernel_and_runs_user_only()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

        var settings = new CaptureSettings
        {
            EnableKernelSession = true,
            EnableUserSession = true,
            UserSessionName = $"TH-Test-{Guid.NewGuid():N}",
        };

        var coordinator = new CaptureCoordinator(settings, new FakePrivilegeProbe(elevated: false));
        await coordinator.StartAsync(CancellationToken.None);

        var status = coordinator.GetStatus();
        status.ProviderStates[ProviderId.KernelProcess].Should().Be(ProviderState.NotConfigured);
        status.ProviderStates[ProviderId.PowerShell].Should().BeOneOf(ProviderState.Starting, ProviderState.Running);

        await coordinator.DisposeAsync();
    }

    [SkippableFact]
    public async Task Channel_exposes_events_to_consumer()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

        var settings = new CaptureSettings { UserSessionName = $"TH-Test-{Guid.NewGuid():N}" };
        var coordinator = new CaptureCoordinator(settings, new FakePrivilegeProbe(elevated: false));
        await coordinator.StartAsync(CancellationToken.None);

        coordinator.Reader.Should().NotBeNull();

        await coordinator.DisposeAsync();
    }
}
```

**Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~CaptureCoordinatorTests --nologo`
Expected: FAIL.

**Step 3: Implement `CaptureCoordinator`**

Create `src/TraceHunter.Capture/CaptureCoordinator.cs`:

```csharp
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Diagnostics.Tracing.Parsers;
using TraceHunter.Core;

namespace TraceHunter.Capture;

[SupportedOSPlatform("windows")]
public sealed class CaptureCoordinator : IAsyncDisposable
{
    private readonly CaptureSettings _settings;
    private readonly IPrivilegeProbe _privilege;
    private readonly Channel<RawEvent> _channel;
    private readonly Dictionary<ProviderId, ProviderState> _states;
    private KernelSessionHost? _kernelHost;
    private UserSessionHost? _userHost;
    private long _eventsObserved;
    private long _eventsDropped;

    public ChannelReader<RawEvent> Reader => _channel.Reader;

    public CaptureCoordinator(CaptureSettings settings, IPrivilegeProbe privilege)
    {
        _settings = settings;
        _privilege = privilege;
        _channel = Channel.CreateBounded<RawEvent>(new BoundedChannelOptions(settings.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _states = settings.EnabledProviders.ToDictionary(p => p, _ => ProviderState.NotConfigured);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Wrap writer so we can count
        var countingWriter = new CountingChannelWriter<RawEvent>(_channel.Writer,
            onWrite: () => Interlocked.Increment(ref _eventsObserved),
            onDrop: () => Interlocked.Increment(ref _eventsDropped));

        if (_settings.EnableKernelSession && _privilege.IsElevated())
        {
            _kernelHost = new KernelSessionHost(countingWriter);
            await _kernelHost.StartAsync(cancellationToken);
            SetStates(new[] { ProviderId.KernelProcess, ProviderId.KernelImage, ProviderId.KernelNetwork }, ProviderState.Running);
        }

        if (_settings.EnableUserSession)
        {
            var userProviders = new List<(Guid, ProviderId)>();
            if (_settings.EnabledProviders.Contains(ProviderId.PowerShell))
                userProviders.Add((Guid.Parse("a0c1853b-5c40-4b15-8766-3cf1c58f985a"), ProviderId.PowerShell));
            if (_settings.EnabledProviders.Contains(ProviderId.DotNetRuntime))
                userProviders.Add((ClrTraceEventParser.ProviderGuid, ProviderId.DotNetRuntime));
            if (_settings.EnabledProviders.Contains(ProviderId.DnsClient))
                userProviders.Add((Guid.Parse("1c95126e-7eea-49a9-a3fe-a378b03ddb4d"), ProviderId.DnsClient));
            if (_settings.EnabledProviders.Contains(ProviderId.WmiActivity))
                userProviders.Add((Guid.Parse("1418ef04-b0b4-4623-bf7e-d74ab47bbdaa"), ProviderId.WmiActivity));

            _userHost = new UserSessionHost(_settings.UserSessionName, userProviders, countingWriter);
            await _userHost.StartAsync(cancellationToken);
            SetStates(userProviders.Select(p => p.Item2), ProviderState.Running);
        }
    }

    public CaptureStatus GetStatus() => new(
        ProviderStates: _states,
        EventsObserved: Interlocked.Read(ref _eventsObserved),
        EventsDropped: Interlocked.Read(ref _eventsDropped));

    public async ValueTask DisposeAsync()
    {
        if (_kernelHost is not null) await _kernelHost.DisposeAsync();
        if (_userHost is not null) await _userHost.DisposeAsync();
        _channel.Writer.TryComplete();
    }

    private void SetStates(IEnumerable<ProviderId> providers, ProviderState state)
    {
        foreach (var p in providers)
        {
            _states[p] = state;
        }
    }

    private sealed class CountingChannelWriter<T>(ChannelWriter<T> inner, Action onWrite, Action onDrop) : ChannelWriter<T>
    {
        public override bool TryWrite(T item)
        {
            if (inner.TryWrite(item)) { onWrite(); return true; }
            onDrop();
            return false;
        }
        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) => inner.WaitToWriteAsync(cancellationToken);
        public override bool TryComplete(Exception? error = null) => inner.TryComplete(error);
    }
}
```

(Provider GUIDs above are well-known Windows ETW provider GUIDs; verify each against `logman query providers` output during execution.)

**Step 4: Run tests**

Run: `dotnet test --filter FullyQualifiedName~CaptureCoordinatorTests --nologo`
Expected: PASS on Windows.

**Step 5: Commit**

```bash
git add src/TraceHunter.Capture tests/TraceHunter.Capture.Tests
git commit -m "feat(capture): add CaptureCoordinator with privilege fallback"
```

---

## Task 6: Host CLI verb `tracehunter capture --raw`

**Files:**
- Modify: `src/TraceHunter.Host/Program.cs`
- Create: `src/TraceHunter.Host/Commands/CaptureCommand.cs`

**Step 1: Wire System.CommandLine root + capture command**

Replace `src/TraceHunter.Host/Program.cs`:

```csharp
using System.CommandLine;
using TraceHunter.Host.Commands;

namespace TraceHunter.Host;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("TraceHunter - .NET-native EDR for Windows");
        root.Subcommands.Add(CaptureCommand.Build());
        return await root.Parse(args).InvokeAsync();
    }
}
```

**Step 2: Implement `CaptureCommand`**

Create `src/TraceHunter.Host/Commands/CaptureCommand.cs`:

```csharp
using System.CommandLine;
using System.Runtime.Versioning;
using System.Text.Json;
using TraceHunter.Capture;
using TraceHunter.Core;

namespace TraceHunter.Host.Commands;

[SupportedOSPlatform("windows")]
internal static class CaptureCommand
{
    public static Command Build()
    {
        var rawOption = new Option<bool>("--raw")
        {
            Description = "Emit raw normalized events as newline-delimited JSON to stdout.",
        };

        var cmd = new Command("capture", "Capture ETW events.");
        cmd.Options.Add(rawOption);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var raw = parseResult.GetValue(rawOption);
            if (!raw)
            {
                Console.Error.WriteLine("capture without --raw is not yet implemented; use --raw.");
                return 2;
            }

            var settings = new CaptureSettings();
            await using var coordinator = new CaptureCoordinator(settings, new PrivilegeProbe());
            await coordinator.StartAsync(ct);

            Console.Error.WriteLine($"Capturing... press Ctrl+C to stop. Status: {JsonSerializer.Serialize(coordinator.GetStatus())}");

            await foreach (var ev in coordinator.Reader.ReadAllAsync(ct))
            {
                Console.WriteLine(JsonSerializer.Serialize(ev));
            }

            return 0;
        });

        return cmd;
    }
}
```

**Step 3: Verify build**

Run: `dotnet build --nologo`
Expected: 0 warnings, 0 errors.

**Step 4: Smoke test (manual)**

In the repo root, in a non-elevated terminal:

```bash
dotnet run --project src/TraceHunter.Host -- capture --raw
```

Expected: starts, prints status to stderr, then begins emitting JSON lines on stdout when ETW events fire (PowerShell events should fire as Windows churns; CLR runtime events fire constantly from the dotnet process itself). Press Ctrl+C to stop.

**Step 5: Commit**

```bash
git add src/TraceHunter.Host
git commit -m "feat(host): add capture --raw CLI verb"
```

---

## Task 7: End-to-end integration test

**Files:**
- Create: `tests/TraceHunter.Integration.Tests/CaptureEndToEndTests.cs`

**Step 1: Write the failing integration test**

```csharp
using System.Diagnostics.Tracing;
using AwesomeAssertions;
using TraceHunter.Capture;
using TraceHunter.Core;
using Xunit;

namespace TraceHunter.Integration.Tests;

[EventSource(Name = "TraceHunter-Test-Integration")]
internal sealed class IntegrationTestEventSource : EventSource
{
    public static readonly IntegrationTestEventSource Log = new();
    public static readonly Guid ProviderGuid = EventSource.GetGuid(typeof(IntegrationTestEventSource));

    [Event(1)]
    public void Hello(string name) => WriteEvent(1, name);
}

public class CaptureEndToEndTests
{
    private sealed class StubProbe(bool elevated) : IPrivilegeProbe
    {
        public bool IsElevated() => elevated;
    }

    [SkippableFact]
    public async Task End_to_end_user_session_captures_synthetic_event()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

        var settings = new CaptureSettings
        {
            EnableKernelSession = false,
            EnableUserSession = true,
            UserSessionName = $"TH-Integration-{Guid.NewGuid():N}",
            EnabledProviders = new HashSet<ProviderId> { ProviderId.PowerShell },
        };

        // We can't add ad-hoc providers via CaptureSettings in v1; use UserSessionHost directly
        var channel = System.Threading.Channels.Channel.CreateBounded<RawEvent>(100);
        await using var host = new UserSessionHost(
            settings.UserSessionName,
            new[] { (IntegrationTestEventSource.ProviderGuid, ProviderId.Unknown) },
            channel.Writer);

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(500);

        IntegrationTestEventSource.Log.Hello("phase-1");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ev = await channel.Reader.ReadAsync(cts.Token);

        ev.PayloadJson.Should().Contain("phase-1");
    }
}
```

**Step 2: Run**

Run: `dotnet test --filter FullyQualifiedName~CaptureEndToEndTests --nologo`
Expected: PASS on Windows; SKIP on non-Windows.

**Step 3: Commit**

```bash
git add tests/TraceHunter.Integration.Tests
git commit -m "test(integration): add end-to-end capture test with synthetic EventSource"
```

---

## Task 8: Phase 1 final verification

**Step 1: Full clean build + tests**

```bash
dotnet clean
dotnet build --configuration Release
dotnet test --configuration Release --no-build
```

Expected:
- Build: 0 warnings, 0 errors, 16 projects.
- Test: all pass on Windows; tests requiring admin will skip when not elevated; 0 fail.

**Step 2: Format check**

```bash
dotnet format --verify-no-changes --no-restore
```
Expected: exits 0.

**Step 3: Smoke test the capture CLI**

```bash
dotnet run --project src/TraceHunter.Host --configuration Release -- capture --raw
```

Let it run for ~5 seconds, then Ctrl+C. Confirm:
- Stderr shows status JSON.
- Stdout emits at least one RawEvent JSON line.
- Process exits cleanly on Ctrl+C.

**Step 4: Confirm git state**

```bash
git status                         # clean
git log --oneline main..HEAD       # ~8 commits
```

**Step 5: Open PR (after pushing)**

```bash
git push -u origin phase-1-capture
gh pr create --base main --head phase-1-capture --title "Phase 1: Capture foundation" --body "..."
```

---

## Phase 1 - Done criteria

- All 8 tasks committed as separate Conventional Commits.
- `dotnet build` clean, `dotnet test` passes (with appropriate skips), `dotnet format` clean.
- `tracehunter capture --raw` runs and emits real ETW events.
- CI green on the PR.

After this phase, Phase 2 (full per-provider normalization into the typed `NormalizedEvent` discriminated union) is the next plan.
