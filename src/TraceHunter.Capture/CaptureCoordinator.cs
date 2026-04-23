using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Diagnostics.Tracing.Parsers;
using TraceHunter.Core;

namespace TraceHunter.Capture;

[SupportedOSPlatform("windows")]
public sealed class CaptureCoordinator : IAsyncDisposable
{
    // Well-known Windows ETW provider GUIDs (verified via `logman query providers`).
    private static readonly Guid PowerShellProviderGuid =
        Guid.Parse("a0c1853b-5c40-4b15-8766-3cf1c58f985a", CultureInfo.InvariantCulture);
    private static readonly Guid DnsClientProviderGuid =
        Guid.Parse("1c95126e-7eea-49a9-a3fe-a378b03ddb4d", CultureInfo.InvariantCulture);
    private static readonly Guid WmiActivityProviderGuid =
        Guid.Parse("1418ef04-b0b4-4623-bf7e-d74ab47bbdaa", CultureInfo.InvariantCulture);

    private readonly CaptureSettings _settings;
    private readonly IPrivilegeProbe _privilege;
    private readonly Channel<RawEvent> _channel;
    private readonly Dictionary<ProviderId, ProviderState> _states;
    private KernelSessionHost? _kernelHost;
    private UserSessionHost? _userHost;
    private long _eventsObserved;
    private long _eventsDropped;

    public ChannelReader<RawEvent> Reader => _channel.Reader;

    public CaptureCoordinator(CaptureSettings settings, IPrivilegeProbe privilege)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(privilege);

        _settings = settings;
        _privilege = privilege;
        _channel = Channel.CreateBounded<RawEvent>(new BoundedChannelOptions(settings.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _states = settings.EnabledProviders.ToDictionary(p => p, _ => ProviderState.NotConfigured);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // ALL ETW session creation requires elevation - if we're not elevated,
        // start nothing. Status will surface NeedsElevation=true so the CLI/UI
        // can emit a helpful message. No exception thrown - graceful inert state.
        if (!_privilege.IsElevated())
        {
            return;
        }

        // Wrap writer so we can count writes and drops without coupling hosts
        // to coordinator-level metrics.
        var countingWriter = new CountingChannelWriter<RawEvent>(
            _channel.Writer,
            onWrite: () => Interlocked.Increment(ref _eventsObserved),
            onDrop: () => Interlocked.Increment(ref _eventsDropped));

        if (_settings.EnableKernelSession)
        {
            _kernelHost = new KernelSessionHost(countingWriter);
            await _kernelHost.StartAsync(cancellationToken).ConfigureAwait(false);
            SetStates(
                new[] { ProviderId.KernelProcess, ProviderId.KernelImage, ProviderId.KernelNetwork },
                ProviderState.Running);
        }

        if (_settings.EnableUserSession)
        {
            var userProviders = new List<(Guid Provider, ProviderId Id)>();
            if (_settings.EnabledProviders.Contains(ProviderId.PowerShell))
            {
                userProviders.Add((PowerShellProviderGuid, ProviderId.PowerShell));
            }
            if (_settings.EnabledProviders.Contains(ProviderId.DotNetRuntime))
            {
                userProviders.Add((ClrTraceEventParser.ProviderGuid, ProviderId.DotNetRuntime));
            }
            if (_settings.EnabledProviders.Contains(ProviderId.DnsClient))
            {
                userProviders.Add((DnsClientProviderGuid, ProviderId.DnsClient));
            }
            if (_settings.EnabledProviders.Contains(ProviderId.WmiActivity))
            {
                userProviders.Add((WmiActivityProviderGuid, ProviderId.WmiActivity));
            }

            _userHost = new UserSessionHost(_settings.UserSessionName, userProviders, countingWriter);
            await _userHost.StartAsync(cancellationToken).ConfigureAwait(false);
            SetStates(userProviders.Select(p => p.Id), ProviderState.Running);
        }
    }

    public CaptureStatus GetStatus() => new(
        ProviderStates: _states.ToImmutableDictionary(),
        EventsObserved: Interlocked.Read(ref _eventsObserved),
        EventsDropped: Interlocked.Read(ref _eventsDropped),
        NeedsElevation: !_privilege.IsElevated());

    public async ValueTask DisposeAsync()
    {
        // Dispose hosts in reverse start order. User-mode session is safe to
        // tear down first; kernel session last. Channel writer completes only
        // after both producers are gone so consumers see a clean end-of-stream.
        if (_userHost is not null)
        {
            await _userHost.DisposeAsync().ConfigureAwait(false);
        }
        if (_kernelHost is not null)
        {
            await _kernelHost.DisposeAsync().ConfigureAwait(false);
        }
        _channel.Writer.TryComplete();
    }

    private void SetStates(IEnumerable<ProviderId> providers, ProviderState state)
    {
        foreach (var p in providers)
        {
            _states[p] = state;
        }
    }

    private sealed class CountingChannelWriter<T>(ChannelWriter<T> inner, Action onWrite, Action onDrop) : ChannelWriter<T>
    {
        public override bool TryWrite(T item)
        {
            // BoundedChannel with DropOldest never reports "full" via TryWrite,
            // so a false return here would only happen post-Complete. Drop counter
            // is reserved for future modes (e.g. DropWrite / Wait). We still
            // increment dropped to honor the contract for any non-DropOldest mode.
            if (inner.TryWrite(item))
            {
                onWrite();
                return true;
            }
            onDrop();
            return false;
        }

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) =>
            inner.WaitToWriteAsync(cancellationToken);

        public override bool TryComplete(Exception? error = null) => inner.TryComplete(error);
    }
}
