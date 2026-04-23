using System.Diagnostics.Tracing;
using System.Threading.Channels;
using AwesomeAssertions;
using TraceHunter.Capture;
using TraceHunter.Core;

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
    [SkippableFact]
    public async Task End_to_end_user_session_captures_synthetic_event()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        Skip.IfNot(new PrivilegeProbe().IsElevated(),
            "Requires elevated process - ETW session creation needs admin or Performance Log Users");

        var sessionName = $"TH-Integration-{Guid.NewGuid():N}";

        // Use UserSessionHost directly so we can register an ad-hoc test EventSource GUID
        // (CaptureCoordinator only knows about the well-known provider GUIDs)
        var channel = Channel.CreateBounded<RawEvent>(100);
        await using var host = new UserSessionHost(
            sessionName,
            new[] { (IntegrationTestEventSource.ProviderGuid, ProviderId.Test) },
            channel.Writer);

        await host.StartAsync(CancellationToken.None);
        await Task.Delay(500);

        IntegrationTestEventSource.Log.Hello("phase-1");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ev = await channel.Reader.ReadAsync(cts.Token);

        ev.PayloadJson.Should().Contain("phase-1");
    }
}
