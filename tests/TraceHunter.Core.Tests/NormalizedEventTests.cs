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
