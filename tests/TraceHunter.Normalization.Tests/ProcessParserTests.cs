using AwesomeAssertions;
using TraceHunter.Core;
using TraceHunter.Normalization;

namespace TraceHunter.Normalization.Tests;

public class ProcessParserTests
{
    [Fact]
    public void Parse_returns_null_for_non_process_events()
    {
        var parser = new ProcessParser();
        var raw = new RawEvent(
            ProviderId.PowerShell, EventId: 4104, Timestamp: DateTimeOffset.UtcNow,
            ProcessId: 1, ThreadId: 2, PayloadJson: "{}");
        parser.Parse(raw).Should().BeNull();
    }

    [Fact]
    public void Parse_translates_kernel_process_start_payload()
    {
        var parser = new ProcessParser();
        var raw = new RawEvent(
            ProviderId.KernelProcess,
            EventId: 1,
            Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 1234,
            ThreadId: 5,
            PayloadJson: """
                {
                    "imageFileName":"notepad.exe",
                    "commandLine":"notepad.exe foo.txt",
                    "parentId":4
                }
                """);

        var result = parser.Parse(raw);

        result.Should().BeOfType<NormalizedEvent.Process>();
        var p = (NormalizedEvent.Process)result!;
        p.Kind.Should().Be(ProcessEventKind.Start);
        p.CommandLine.Should().Be("notepad.exe foo.txt");
        p.ParentProcessId.Should().Be(4);
        p.ProcessImage.Should().Be("notepad.exe");
    }
}
