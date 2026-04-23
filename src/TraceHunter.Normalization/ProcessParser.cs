using System.Text.Json;
using TraceHunter.Core;

namespace TraceHunter.Normalization;

public sealed class ProcessParser : INormalizedParser
{
    public bool CanParse(ProviderId providerId) => providerId == ProviderId.KernelProcess;

    public NormalizedEvent? Parse(RawEvent raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (!CanParse(raw.ProviderId)) return null;

        using var doc = JsonDocument.Parse(raw.PayloadJson);
        var root = doc.RootElement;

        var image = root.TryGetProperty("imageFileName", out var img) ? img.GetString() ?? "" : "";
        var cmdline = root.TryGetProperty("commandLine", out var cmd) ? cmd.GetString() ?? "" : "";
        var parentId = root.TryGetProperty("parentId", out var pp) && pp.TryGetInt32(out var p) ? p : 0;

        // ProcessStart and ProcessStop typically come through as separate event IDs;
        // distinguish by EventID. For Phase 2, treat anything that's not Stop as Start.
        var kind = raw.EventId switch
        {
            2 => ProcessEventKind.Exit,
            _ => ProcessEventKind.Start,
        };

        return new NormalizedEvent.Process(
            Timestamp: raw.Timestamp,
            ProcessId: raw.ProcessId,
            ParentProcessId: parentId,
            ThreadId: raw.ThreadId,
            ProcessImage: image,
            UserSid: null,
            Host: Environment.MachineName,
            Kind: kind,
            CommandLine: cmdline,
            ImagePath: image, // Kernel events often only have the file name; full path comes from enrichment in Phase 3
            Integrity: null);
    }
}
