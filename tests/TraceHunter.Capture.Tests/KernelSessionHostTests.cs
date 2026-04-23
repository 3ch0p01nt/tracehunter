using System.Threading.Channels;
using AwesomeAssertions;
using TraceHunter.Core;

namespace TraceHunter.Capture.Tests;

public class KernelSessionHostTests
{
    [SkippableFact]
    public async Task StartAsync_when_admin_emits_process_events()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        Skip.IfNot(new PrivilegeProbe().IsElevated(),
            "Requires elevated process - kernel ETW session creation needs admin");

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
