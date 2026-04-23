using AwesomeAssertions;
using TraceHunter.Core;
using TraceHunter.Normalization;

namespace TraceHunter.Normalization.Tests;

public class NetworkParserTests
{
    [Fact]
    public void Parse_returns_null_for_non_network_events()
    {
        var parser = new NetworkParser();
        var raw = new RawEvent(
            ProviderId.PowerShell, EventId: 4104, Timestamp: DateTimeOffset.UtcNow,
            ProcessId: 1, ThreadId: 2, PayloadJson: "{}");
        parser.Parse(raw).Should().BeNull();
    }

    [Fact]
    public void Parse_translates_outbound_tcp_connect_payload()
    {
        var parser = new NetworkParser();
        var raw = new RawEvent(
            ProviderId.KernelNetwork,
            EventId: 12,
            Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 1234,
            ThreadId: 5,
            PayloadJson: """
                {
                    "source":"192.168.1.10:49152",
                    "dest":"8.8.8.8:443"
                }
                """);

        var result = parser.Parse(raw);

        result.Should().BeOfType<NormalizedEvent.Network>();
        var n = (NormalizedEvent.Network)result!;
        n.Direction.Should().Be(NetworkDirection.Outbound);
        n.Protocol.Should().Be("TCP");
        n.LocalAddress.Should().Be("192.168.1.10");
        n.LocalPort.Should().Be(49152);
        n.RemoteAddress.Should().Be("8.8.8.8");
        n.RemotePort.Should().Be(443);
    }
}
