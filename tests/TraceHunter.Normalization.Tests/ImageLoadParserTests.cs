using AwesomeAssertions;
using TraceHunter.Core;
using TraceHunter.Normalization;

namespace TraceHunter.Normalization.Tests;

public class ImageLoadParserTests
{
    [Fact]
    public void Parse_returns_null_for_non_image_events()
    {
        var parser = new ImageLoadParser();
        var raw = new RawEvent(
            ProviderId.PowerShell, EventId: 4104, Timestamp: DateTimeOffset.UtcNow,
            ProcessId: 1, ThreadId: 2, PayloadJson: "{}");
        parser.Parse(raw).Should().BeNull();
    }

    [Fact]
    public void Parse_translates_kernel_image_load_payload()
    {
        var parser = new ImageLoadParser();
        var raw = new RawEvent(
            ProviderId.KernelImage,
            EventId: 10,
            Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 1234,
            ThreadId: 5,
            PayloadJson: """
                {
                    "fileName":"C:\\Windows\\System32\\ntdll.dll",
                    "imageBase":"7FFE12340000",
                    "imageSize":2097152
                }
                """);

        var result = parser.Parse(raw);

        result.Should().BeOfType<NormalizedEvent.ImageLoad>();
        var i = (NormalizedEvent.ImageLoad)result!;
        i.ImagePath.Should().Be(@"C:\Windows\System32\ntdll.dll");
        i.ImageBase.Should().Be(0x7FFE12340000UL);
        i.ImageSize.Should().Be(2097152);
        i.ProcessId.Should().Be(1234);
        i.ThreadId.Should().Be(5);
    }
}
