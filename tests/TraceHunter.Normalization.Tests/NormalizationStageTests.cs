using System.Threading.Channels;
using AwesomeAssertions;
using TraceHunter.Core;
using TraceHunter.Normalization;

namespace TraceHunter.Normalization.Tests;

public class NormalizationStageTests
{
    private static IEnumerable<INormalizedParser> AllParsers() => new INormalizedParser[]
    {
        new ProcessParser(),
        new ImageLoadParser(),
        new NetworkParser(),
        new ScriptParser(),
        new RuntimeParser(),
        new DnsParser(),
    };

    [Fact]
    public void ParserRegistry_resolves_correct_parser_per_provider_id()
    {
        var registry = new ParserRegistry(AllParsers());

        registry.Resolve(ProviderId.KernelProcess).Should().BeOfType<ProcessParser>();
        registry.Resolve(ProviderId.KernelImage).Should().BeOfType<ImageLoadParser>();
        registry.Resolve(ProviderId.KernelNetwork).Should().BeOfType<NetworkParser>();
        registry.Resolve(ProviderId.PowerShell).Should().BeOfType<ScriptParser>();
        registry.Resolve(ProviderId.DotNetRuntime).Should().BeOfType<RuntimeParser>();
        registry.Resolve(ProviderId.DnsClient).Should().BeOfType<DnsParser>();

        // Providers without a registered parser return null.
        registry.Resolve(ProviderId.Unknown).Should().BeNull();
        registry.Resolve(ProviderId.Test).Should().BeNull();
        registry.Resolve(ProviderId.WmiActivity).Should().BeNull();
    }

    [Fact]
    public async Task NormalizationStage_dispatches_raw_events_through_registered_parsers()
    {
        var input = Channel.CreateUnbounded<RawEvent>();
        var output = Channel.CreateUnbounded<NormalizedEvent>();
        var registry = new ParserRegistry(AllParsers());

        await using var stage = new NormalizationStage(registry, input.Reader, output.Writer);
        await stage.StartAsync(CancellationToken.None);

        // 1) Process event - should produce a NormalizedEvent.Process
        input.Writer.TryWrite(new RawEvent(
            ProviderId.KernelProcess, EventId: 1, Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 1234, ThreadId: 5,
            PayloadJson: """{"imageFileName":"notepad.exe","commandLine":"notepad.exe","parentId":4}"""));

        // 2) Network event - should produce a NormalizedEvent.Network
        input.Writer.TryWrite(new RawEvent(
            ProviderId.KernelNetwork, EventId: 1, Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 4321, ThreadId: 6,
            PayloadJson: """{"source":"192.168.1.10:49152","dest":"8.8.8.8:443"}"""));

        // 3) Unknown provider - should produce nothing
        input.Writer.TryWrite(new RawEvent(
            ProviderId.Unknown, EventId: 0, Timestamp: DateTimeOffset.UnixEpoch,
            ProcessId: 0, ThreadId: 0, PayloadJson: "{}"));

        input.Writer.Complete();

        // Collect with a timeout so a hang is a failure rather than a hang.
        var collected = new List<NormalizedEvent>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await foreach (var ev in output.Reader.ReadAllAsync(cts.Token))
            {
                collected.Add(ev);
                if (collected.Count >= 2) break;
            }
        }
        catch (OperationCanceledException)
        {
            // Drained what we got within the timeout window.
        }

        collected.Should().HaveCount(2);
        collected[0].Should().BeOfType<NormalizedEvent.Process>();
        collected[1].Should().BeOfType<NormalizedEvent.Network>();
    }

    [Fact]
    public async Task NormalizationStage_disposes_cleanly_with_no_hang()
    {
        var input = Channel.CreateUnbounded<RawEvent>();
        var output = Channel.CreateUnbounded<NormalizedEvent>();
        var registry = new ParserRegistry(AllParsers());

        var stage = new NormalizationStage(registry, input.Reader, output.Writer);
        await stage.StartAsync(CancellationToken.None);

        // DisposeAsync should cancel the loop and complete the writer without hanging.
        var disposeTask = stage.DisposeAsync().AsTask();
        var winner = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(5)));
        winner.Should().BeSameAs(disposeTask, "DisposeAsync must complete within the timeout");
        await disposeTask; // surface any exception from the disposal path

        // Writer should be completed - reader's completion task should have finished.
        var completion = output.Reader.Completion;
        var completionWinner = await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(1)));
        completionWinner.Should().BeSameAs(completion, "writer must be completed after DisposeAsync");
    }
}
