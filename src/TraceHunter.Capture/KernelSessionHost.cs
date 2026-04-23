using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using TraceHunter.Core;

namespace TraceHunter.Capture;

[SupportedOSPlatform("windows")]
public sealed class KernelSessionHost : ISessionHost
{
    private readonly ChannelWriter<RawEvent> _writer;
    private TraceEventSession? _session;
    private Task? _dispatchTask;

    public KernelSessionHost(ChannelWriter<RawEvent> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        _writer = writer;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Kernel sessions MUST use the literal name "NT Kernel Logger".
        // Only one such session exists per machine; if a previous TraceHunter
        // run leaked, force-stop it before starting our own.
        var sessionName = KernelTraceEventParser.KernelSessionName;

        if (TraceEventSession.GetActiveSessionNames().Contains(sessionName))
        {
            using var leaked = new TraceEventSession(sessionName) { StopOnDispose = true };
            leaked.Stop();
        }

        _session = new TraceEventSession(sessionName);

        // Kernel providers are part of the OS, so EnableKernelProvider does not
        // fail with manifest issues. If it does fail, that IS session-fatal,
        // so we let the exception propagate (no per-provider try/catch needed).
        _session.EnableKernelProvider(
            KernelTraceEventParser.Keywords.Process |
            KernelTraceEventParser.Keywords.ImageLoad |
            KernelTraceEventParser.Keywords.NetworkTCPIP);

        _session.Source.Kernel.ProcessStart += OnProcessStart;
        _session.Source.Kernel.ImageLoad += OnImageLoad;
        _session.Source.Kernel.TcpIpConnect += OnTcpIpConnect;

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

    private void OnProcessStart(ProcessTraceData data)
    {
        EmitRaw(
            ProviderId.KernelProcess,
            (int)data.ID,
            data.TimeStamp,
            data.ProcessID,
            data.ThreadID,
            new
            {
                imageFileName = data.ImageFileName,
                commandLine = data.CommandLine,
                parentId = data.ParentID,
            });
    }

    private void OnImageLoad(ImageLoadTraceData data)
    {
        EmitRaw(
            ProviderId.KernelImage,
            (int)data.ID,
            data.TimeStamp,
            data.ProcessID,
            data.ThreadID,
            new
            {
                fileName = data.FileName,
                imageBase = data.ImageBase.ToString("X", System.Globalization.CultureInfo.InvariantCulture),
                imageSize = data.ImageSize,
            });
    }

    private void OnTcpIpConnect(TcpIpConnectTraceData data)
    {
        EmitRaw(
            ProviderId.KernelNetwork,
            (int)data.ID,
            data.TimeStamp,
            data.ProcessID,
            data.ThreadID,
            new
            {
                source = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{data.saddr}:{data.sport}"),
                dest = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{data.daddr}:{data.dport}"),
            });
    }

    private void EmitRaw(ProviderId provider, int eventId, DateTime timestamp, int pid, int tid, object payload)
    {
        var ev = new RawEvent(
            ProviderId: provider,
            EventId: eventId,
            Timestamp: new DateTimeOffset(timestamp, TimeSpan.Zero),
            ProcessId: pid >= 0 ? pid : 0,
            ThreadId: tid >= 0 ? tid : 0,
            PayloadJson: JsonSerializer.Serialize(payload));
        _writer.TryWrite(ev);
    }
}
