using System.Globalization;
using System.Text.Json;
using TraceHunter.Core;

namespace TraceHunter.Normalization;

public sealed class NetworkParser : INormalizedParser
{
    public bool CanParse(ProviderId providerId) => providerId == ProviderId.KernelNetwork;

    public NormalizedEvent? Parse(RawEvent raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (!CanParse(raw.ProviderId)) return null;

        using var doc = JsonDocument.Parse(raw.PayloadJson);
        var root = doc.RootElement;

        var source = root.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "";
        var dest = root.TryGetProperty("dest", out var dst) ? dst.GetString() ?? "" : "";

        var (la, lp) = ParseEndpoint(source);
        var (ra, rp) = ParseEndpoint(dest);

        return new NormalizedEvent.Network(
            Timestamp: raw.Timestamp,
            ProcessId: raw.ProcessId,
            ParentProcessId: 0,
            ThreadId: raw.ThreadId,
            ProcessImage: "",
            UserSid: null,
            Host: Environment.MachineName,
            Direction: NetworkDirection.Outbound, // TcpIpConnect is outbound; Accept is inbound (separate handler)
            Protocol: "TCP",
            LocalAddress: la,
            LocalPort: lp,
            RemoteAddress: ra,
            RemotePort: rp);
    }

    private static (string addr, int port) ParseEndpoint(string s)
    {
        var idx = s.LastIndexOf(':');
        if (idx < 0) return (s, 0);
        var addr = s[..idx];
        var portText = s[(idx + 1)..];
        return (addr, int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0);
    }
}
