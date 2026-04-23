using System.Globalization;
using System.Text.Json;
using TraceHunter.Core;

namespace TraceHunter.Normalization;

public sealed class RuntimeParser : INormalizedParser
{
    public bool CanParse(ProviderId providerId) => providerId == ProviderId.DotNetRuntime;

    public NormalizedEvent? Parse(RawEvent raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (!CanParse(raw.ProviderId)) return null;

        using var doc = JsonDocument.Parse(raw.PayloadJson);
        var root = doc.RootElement;

        var (kind, detail) = MapEvent(raw.EventId, root);

        return new NormalizedEvent.Runtime(
            Timestamp: raw.Timestamp,
            ProcessId: raw.ProcessId,
            ParentProcessId: 0,
            ThreadId: raw.ThreadId,
            ProcessImage: "",
            UserSid: null,
            Host: Environment.MachineName,
            Kind: kind,
            Detail: detail);
    }

    private static (RuntimeEventKind kind, string detail) MapEvent(int eventId, JsonElement payload)
    {
        // Exception
        if (eventId == 1)
        {
            var exType = payload.TryGetProperty("ExceptionType", out var t) ? t.GetString() ?? "" : "";
            var exMsg = payload.TryGetProperty("ExceptionMessage", out var m) ? m.GetString() ?? "" : "";
            return (RuntimeEventKind.Exception, string.Create(CultureInfo.InvariantCulture, $"{exType}: {exMsg}"));
        }

        // GC
        if (eventId >= 2 && eventId <= 7)
        {
            return (RuntimeEventKind.Gc, string.Create(CultureInfo.InvariantCulture, $"GC event {eventId}"));
        }

        // ThreadPool
        if (eventId >= 50 && eventId <= 60)
        {
            return (RuntimeEventKind.ThreadPool, string.Create(CultureInfo.InvariantCulture, $"ThreadPool event {eventId}"));
        }

        // JIT
        if (eventId >= 140 && eventId <= 150)
        {
            return (RuntimeEventKind.Jit, string.Create(CultureInfo.InvariantCulture, $"JIT event {eventId}"));
        }

        // AssemblyLoad
        if (eventId == 154)
        {
            var name = payload.TryGetProperty("AssemblyName", out var n) ? n.GetString() ?? "" : "";
            return (RuntimeEventKind.AssemblyLoad, name);
        }

        return (RuntimeEventKind.Other, string.Create(CultureInfo.InvariantCulture, $"EventId={eventId}"));
    }
}
