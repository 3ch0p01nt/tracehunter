using System.Text.Json;
using TraceHunter.Core;

namespace TraceHunter.Normalization;

public sealed class ScriptParser : INormalizedParser
{
    public bool CanParse(ProviderId providerId) => providerId == ProviderId.PowerShell;

    public NormalizedEvent? Parse(RawEvent raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (!CanParse(raw.ProviderId)) return null;
        if (raw.EventId != 4104) return null;  // ScriptBlockLogging only

        using var doc = JsonDocument.Parse(raw.PayloadJson);
        var root = doc.RootElement;

        var scriptBlockId = root.TryGetProperty("ScriptBlockId", out var sb) ? sb.GetString() ?? "" : "";
        var content = root.TryGetProperty("ScriptBlockText", out var st) ? st.GetString() ?? "" : "";

        return new NormalizedEvent.Script(
            Timestamp: raw.Timestamp,
            ProcessId: raw.ProcessId,
            ParentProcessId: 0,
            ThreadId: raw.ThreadId,
            ProcessImage: "",
            UserSid: null,
            Host: Environment.MachineName,
            ScriptBlockId: scriptBlockId,
            Content: content);
    }
}
