using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using TraceHunter.Core;

namespace TraceHunter.Capture;

[SupportedOSPlatform("windows")]
public sealed class UserSessionHost : ISessionHost
{
    private readonly string _sessionName;
    private readonly IReadOnlyCollection<(Guid Provider, ProviderId Id)> _providers;
    private readonly ChannelWriter<RawEvent> _writer;
    private TraceEventSession? _session;
    private Task? _dispatchTask;

    public UserSessionHost(
        string sessionName,
        IEnumerable<(Guid Provider, ProviderId Id)> providers,
        ChannelWriter<RawEvent> writer)
    {
        ArgumentNullException.ThrowIfNull(sessionName);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(writer);

        _sessionName = sessionName;
        _providers = providers.ToList();
        _writer = writer;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Clean up any leaked session with the same name (sessions persist across process death)
        if (TraceEventSession.GetActiveSessionNames().Contains(_sessionName))
        {
            using var leaked = new TraceEventSession(_sessionName) { StopOnDispose = true };
            leaked.Stop();
        }

        _session = new TraceEventSession(_sessionName);
        var providerLookup = _providers.ToDictionary(p => p.Provider, p => p.Id);

        _session.Source.Dynamic.All += data =>
        {
            if (!providerLookup.TryGetValue(data.ProviderGuid, out var providerId))
            {
                return;
            }

            var payload = SerializePayload(data);
            var ev = new RawEvent(
                ProviderId: providerId,
                EventId: (int)data.ID,
                Timestamp: data.TimeStamp,
                ProcessId: data.ProcessID >= 0 ? data.ProcessID : 0,
                ThreadId: data.ThreadID >= 0 ? data.ThreadID : 0,
                PayloadJson: payload);

            _writer.TryWrite(ev);
        };

        foreach (var (providerGuid, _) in _providers)
        {
            try
            {
                _session.EnableProvider(providerGuid);
            }
#pragma warning disable CA1031 // Do not catch general exception types - per-provider failure tolerated
            catch
            {
                // Per-provider failure tolerated; provider will simply not emit.
                // We do not fail the whole session if one provider's manifest is missing.
            }
#pragma warning restore CA1031
        }

        _dispatchTask = Task.Run(() => _session.Source.Process(), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _session?.Stop();
        if (_dispatchTask is not null)
        {
            try
            {
                await _dispatchTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Dispatch loop exit best-effort.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _session?.Dispose();
    }

    private static string SerializePayload(TraceEvent data)
    {
        var dict = new Dictionary<string, object?>(data.PayloadNames.Length);
        for (int i = 0; i < data.PayloadNames.Length; i++)
        {
            dict[data.PayloadNames[i]] = data.PayloadValue(i);
        }
        return JsonSerializer.Serialize(dict);
    }
}
