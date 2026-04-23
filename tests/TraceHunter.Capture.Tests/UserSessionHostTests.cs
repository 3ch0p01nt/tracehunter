using System.Threading.Channels;
using AwesomeAssertions;
using TraceHunter.Core;

namespace TraceHunter.Capture.Tests;

public class UserSessionHostTests
{
    [SkippableFact]
    public async Task StartAsync_then_Ping_event_arrives_in_channel()
    {
        // Skip if not Windows (TraceEvent only works on Windows)
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        Skip.IfNot(new PrivilegeProbe().IsElevated(),
            "Requires elevated process - Windows ETW session creation needs admin or Performance Log Users membership");

        var sessionName = $"TH-Test-{Guid.NewGuid():N}";
        var channel = Channel.CreateBounded<RawEvent>(100);

        await using var host = new UserSessionHost(
            sessionName,
            new[] { (TestEventSource.ProviderGuid, ProviderId.Test) },
            channel.Writer);

        await host.StartAsync(CancellationToken.None);

        // Give the dispatch loop a moment to attach
        await Task.Delay(500);

        TestEventSource.Log.Ping("hello");

        // Loop ReadAllAsync and accept the first event whose payload matches "hello".
        // Hardens against any other ETW noise that might land on the test session.
        RawEvent? matched = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (var ev in channel.Reader.ReadAllAsync(cts.Token))
            {
                if (ev.PayloadJson.Contains("hello", StringComparison.Ordinal))
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
        matched.PayloadJson.Should().Contain("hello");
    }
}
