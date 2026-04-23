using System.Collections.Immutable;

namespace TraceHunter.Core;

// ProviderStates is a snapshot. Use ImmutableDictionary so consumers
// holding a CaptureStatus past the next state transition see a stable view.
public sealed record CaptureStatus(
    ImmutableDictionary<ProviderId, ProviderState> ProviderStates,
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
