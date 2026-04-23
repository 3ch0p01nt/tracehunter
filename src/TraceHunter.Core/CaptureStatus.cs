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
