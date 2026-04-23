using AwesomeAssertions;
using TraceHunter.Core;

namespace TraceHunter.Core.Tests;

public class RawEventTests
{
    [Fact]
    public void Constructor_sets_all_fields()
    {
        var ts = DateTimeOffset.UtcNow;
        var ev = new RawEvent(
            ProviderId: ProviderId.KernelProcess,
            EventId: 1,
            Timestamp: ts,
            ProcessId: 1234,
            ThreadId: 5678,
            PayloadJson: """{"image":"notepad.exe"}""");

        ev.ProviderId.Should().Be(ProviderId.KernelProcess);
        ev.EventId.Should().Be(1);
        ev.Timestamp.Should().Be(ts);
        ev.ProcessId.Should().Be(1234);
        ev.ThreadId.Should().Be(5678);
        ev.PayloadJson.Should().Be("""{"image":"notepad.exe"}""");
    }

    [Fact]
    public void Constructor_rejects_negative_pid()
    {
        var act = () => new RawEvent(
            ProviderId.KernelProcess, EventId: 1, Timestamp: DateTimeOffset.UtcNow,
            ProcessId: -1, ThreadId: 0, PayloadJson: "{}");

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ProcessId");
    }

    [Fact]
    public void Constructor_rejects_null_payload()
    {
        var act = () => new RawEvent(
            ProviderId.KernelProcess, EventId: 1, Timestamp: DateTimeOffset.UtcNow,
            ProcessId: 0, ThreadId: 0, PayloadJson: null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
