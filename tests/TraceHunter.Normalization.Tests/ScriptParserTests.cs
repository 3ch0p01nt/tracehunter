using AwesomeAssertions;
using TraceHunter.Core;
using TraceHunter.Normalization;

namespace TraceHunter.Normalization.Tests;

public class ScriptParserTests
{
    [Fact]
    public void Parse_returns_null_for_non_powershell_events()
    {
        var parser = new ScriptParser();
        var raw = new RawEvent(
            ProviderId.KernelProcess, EventId: 4104, Timestamp: DateTimeOffset.UtcNow,
            ProcessId: 1, ThreadId: 2, PayloadJson: "{}");
        parser.Parse(raw).Should().BeNull();
    }

    [Fact]
    public void Parse_returns_null_for_powershell_event_other_than_4104()
    {
        var parser = new ScriptParser();
        var raw = new RawEvent(
            ProviderId.PowerShell, EventId: 4100, Timestamp: DateTimeOffset.UtcNow,
            ProcessId: 1, ThreadId: 2, PayloadJson: "{}");
        parser.Parse(raw).Should().BeNull();
    }

    [Fact]
    public void Parse_translates_scriptblock_logging_payload()
    {
        var parser = new ScriptParser();
        var raw = new RawEvent(
            ProviderId.PowerShell,
            EventId: 4104,
            Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 1234,
            ThreadId: 5,
            PayloadJson: """
                {
                    "MessageNumber":1,
                    "MessageTotal":1,
                    "ScriptBlockId":"a3f2d1e0-1111-2222-3333-444455556666",
                    "ScriptBlockText":"Get-Process | Where-Object { $_.CPU -gt 100 }"
                }
                """);

        var result = parser.Parse(raw);

        result.Should().BeOfType<NormalizedEvent.Script>();
        var s = (NormalizedEvent.Script)result!;
        s.ScriptBlockId.Should().Be("a3f2d1e0-1111-2222-3333-444455556666");
        s.Content.Should().Be("Get-Process | Where-Object { $_.CPU -gt 100 }");
        s.ProcessId.Should().Be(1234);
    }
}
