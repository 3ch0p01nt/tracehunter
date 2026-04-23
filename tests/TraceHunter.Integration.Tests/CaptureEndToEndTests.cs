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

        // Loop ReadAllAsync and accept the first event whose payload matches "phase-1".
        // Hardens against any other ETW noise that might land on the test session.
        RawEvent? matched = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (var ev in channel.Reader.ReadAllAsync(cts.Token))
            {
                if (ev.PayloadJson.Contains("phase-1", StringComparison.Ordinal))
                {
                    matched = ev;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out without finding a match; matched stays null and assertion fails.
        }

        matched.Should().NotBeNull();
        matched!.ProviderId.Should().Be(ProviderId.Test);
        matched.EventId.Should().Be(1);
        matched.PayloadJson.Should().Contain("phase-1");
    }
}
