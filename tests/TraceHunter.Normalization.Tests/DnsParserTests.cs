using AwesomeAssertions;
using TraceHunter.Core;
using TraceHunter.Normalization;

namespace TraceHunter.Normalization.Tests;

public class DnsParserTests
{
    [Fact]
    public void Parse_returns_null_for_non_dns_events()
    {
        var parser = new DnsParser();
        var raw = new RawEvent(
            ProviderId.PowerShell, EventId: 4104, Timestamp: DateTimeOffset.UtcNow,
            ProcessId: 1, ThreadId: 2, PayloadJson: "{}");
        parser.Parse(raw).Should().BeNull();
    }

    [Fact]
    public void Parse_translates_dns_query_event()
    {
        var parser = new DnsParser();
        var raw = new RawEvent(
            ProviderId.DnsClient,
            EventId: 3006,
            Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 1234,
            ThreadId: 5,
            PayloadJson: """
                {
                    "QueryName":"www.example.com",
                    "QueryType":"1",
                    "QueryResults":""
                }
                """);

        var result = parser.Parse(raw);

        result.Should().BeOfType<NormalizedEvent.Dns>();
        var d = (NormalizedEvent.Dns)result!;
        d.Kind.Should().Be(DnsEventKind.Query);
        d.QueryName.Should().Be("www.example.com");
        d.QueryType.Should().Be("1");
        d.Result.Should().Be("");
    }

    [Fact]
    public void Parse_translates_dns_response_event()
    {
        var parser = new DnsParser();
        var raw = new RawEvent(
            ProviderId.DnsClient,
            EventId: 3008,
            Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 1234,
            ThreadId: 5,
            PayloadJson: """
                {
                    "QueryName":"www.example.com",
                    "QueryType":"1",
                    "QueryResults":"93.184.216.34;"
                }
                """);

        var result = parser.Parse(raw);

        result.Should().BeOfType<NormalizedEvent.Dns>();
        var d = (NormalizedEvent.Dns)result!;
        d.Kind.Should().Be(DnsEventKind.Response);
        d.QueryName.Should().Be("www.example.com");
        d.QueryType.Should().Be("1");
        d.Result.Should().Be("93.184.216.34;");
    }
}
