using System.Text.Json;
using TraceHunter.Core;

namespace TraceHunter.Normalization;

public sealed class DnsParser : INormalizedParser
{
    public bool CanParse(ProviderId providerId) => providerId == ProviderId.DnsClient;

    public NormalizedEvent? Parse(RawEvent raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (!CanParse(raw.ProviderId)) return null;

        var kind = raw.EventId switch
        {
            3006 => DnsEventKind.Query,
            3008 => DnsEventKind.Response,
            _ => (DnsEventKind?)null,
        };
        if (kind is null) return null;

        using var doc = JsonDocument.Parse(raw.PayloadJson);
        var root = doc.RootElement;

        var queryName = root.TryGetProperty("QueryName", out var qn) ? qn.GetString() ?? "" : "";
        var queryType = root.TryGetProperty("QueryType", out var qt) ? qt.GetString() : null;
        var result = root.TryGetProperty("QueryResults", out var qr) ? qr.GetString() : null;

        return new NormalizedEvent.Dns(
            Timestamp: raw.Timestamp,
            ProcessId: raw.ProcessId,
            ParentProcessId: 0,
            ThreadId: raw.ThreadId,
            ProcessImage: "",
            UserSid: null,
            Host: Environment.MachineName,
            Kind: kind.Value,
            QueryName: queryName,
            QueryType: queryType,
            Result: result);
    }
}
