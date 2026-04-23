using System.Diagnostics.Tracing;

namespace TraceHunter.Capture.Tests;

[EventSource(Name = "TraceHunter-Test-Capture")]
internal sealed class TestEventSource : EventSource
{
    public static readonly TestEventSource Log = new();
    public static readonly Guid ProviderGuid = EventSource.GetGuid(typeof(TestEventSource));

    [Event(1, Level = EventLevel.Informational)]
    public void Ping(string message) => WriteEvent(1, message);
}
