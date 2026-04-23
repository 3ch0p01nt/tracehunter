using System.Collections.Immutable;

namespace TraceHunter.Core;

// ProviderStates is a snapshot. Use ImmutableDictionary so consumers
// holding a CaptureStatus past the next state transition see a stable view.
// NeedsElevation surfaces the privilege gate result so downstream consumers
// (CLI, UI) can render a clear remediation message without re-probing.
public sealed record CaptureStatus(
    ImmutableDictionary<ProviderId, ProviderState> ProviderStates,
    long EventsObserved,
    long EventsDropped,
    bool NeedsElevation);

public enum ProviderState
{
    NotConfigured,
    Starting,
    Running,
    Failed,
    Stopped,
}
