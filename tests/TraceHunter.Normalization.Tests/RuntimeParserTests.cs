using AwesomeAssertions;
using TraceHunter.Core;
using TraceHunter.Normalization;

namespace TraceHunter.Normalization.Tests;

public class RuntimeParserTests
{
    [Fact]
    public void Parse_returns_null_for_non_clr_events()
    {
        var parser = new RuntimeParser();
        var raw = new RawEvent(
            ProviderId.PowerShell, EventId: 4104, Timestamp: DateTimeOffset.UtcNow,
            ProcessId: 1, ThreadId: 2, PayloadJson: "{}");
        parser.Parse(raw).Should().BeNull();
    }

    [Fact]
    public void Parse_translates_clr_exception_event()
    {
        var parser = new RuntimeParser();
        var raw = new RawEvent(
            ProviderId.DotNetRuntime,
            EventId: 1,
            Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 1234,
            ThreadId: 5,
            PayloadJson: """
                {
                    "ExceptionType":"System.InvalidOperationException",
                    "ExceptionMessage":"Sequence contains no elements"
                }
                """);

        var result = parser.Parse(raw);

        result.Should().BeOfType<NormalizedEvent.Runtime>();
        var r = (NormalizedEvent.Runtime)result!;
        r.Kind.Should().Be(RuntimeEventKind.Exception);
        r.Detail.Should().Be("System.InvalidOperationException: Sequence contains no elements");
    }

    [Fact]
    public void Parse_translates_clr_assembly_load_event()
    {
        var parser = new RuntimeParser();
        var raw = new RawEvent(
            ProviderId.DotNetRuntime,
            EventId: 154,
            Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 1234,
            ThreadId: 5,
            PayloadJson: """
                {
                    "AssemblyName":"System.Text.Json, Version=8.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51"
                }
                """);

        var result = parser.Parse(raw);

        result.Should().BeOfType<NormalizedEvent.Runtime>();
        var r = (NormalizedEvent.Runtime)result!;
        r.Kind.Should().Be(RuntimeEventKind.AssemblyLoad);
        r.Detail.Should().Be("System.Text.Json, Version=8.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
    }
}
