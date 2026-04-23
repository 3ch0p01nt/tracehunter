using System.Threading.Channels;
using TraceHunter.Core;

namespace TraceHunter.Normalization;

/// <summary>
/// Pumps <see cref="RawEvent"/> values from an input channel through a
/// <see cref="ParserRegistry"/> and writes resulting <see cref="NormalizedEvent"/>
/// values to an output channel. The dispatch loop runs on <see cref="Task.Run"/>
/// until cancelled. Disposal is cooperative: the loop is cancelled, awaited
/// with a short timeout, and the output writer is completed so downstream
/// readers see end-of-stream.
/// </summary>
public sealed class NormalizationStage : IAsyncDisposable
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);

    private readonly ParserRegistry _registry;
    private readonly ChannelReader<RawEvent> _input;
    private readonly ChannelWriter<NormalizedEvent> _output;
    private Task? _loop;
    private CancellationTokenSource? _cts;

    public NormalizationStage(
        ParserRegistry registry,
        ChannelReader<RawEvent> input,
        ChannelWriter<NormalizedEvent> output)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        _registry = registry;
        _input = input;
        _output = output;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        _loop = Task.Run(() => RunLoop(token), token);
        return Task.CompletedTask;
    }

    private async Task RunLoop(CancellationToken ct)
    {
        try
        {
            await foreach (var raw in _input.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var parser = _registry.Resolve(raw.ProviderId);
                if (parser is null)
                {
                    continue;
                }
                var normalized = parser.Parse(raw);
                if (normalized is null)
                {
                    continue;
                }
                _output.TryWrite(normalized);
            }
        }
        catch (OperationCanceledException)
        {
            // Clean shutdown - cancellation is the normal stop signal.
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed - nothing to do.
        }

        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(DisposeTimeout).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on cooperative shutdown.
            }
            catch (TimeoutException)
            {
                // Loop did not stop within the budget; complete the writer anyway.
            }
        }

        _cts?.Dispose();
        _output.TryComplete();
    }
}
