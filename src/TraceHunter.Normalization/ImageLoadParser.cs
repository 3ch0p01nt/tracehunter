using System.Globalization;
using System.Text.Json;
using TraceHunter.Core;

namespace TraceHunter.Normalization;

public sealed class ImageLoadParser : INormalizedParser
{
    public bool CanParse(ProviderId providerId) => providerId == ProviderId.KernelImage;

    public NormalizedEvent? Parse(RawEvent raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (!CanParse(raw.ProviderId)) return null;

        using var doc = JsonDocument.Parse(raw.PayloadJson);
        var root = doc.RootElement;

        var fileName = root.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
        var imageBaseHex = root.TryGetProperty("imageBase", out var ib) ? ib.GetString() ?? "0" : "0";
        var imageSize = root.TryGetProperty("imageSize", out var sz) && sz.TryGetInt32(out var s) ? s : 0;

        var imageBase = ulong.Parse(imageBaseHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return new NormalizedEvent.ImageLoad(
            Timestamp: raw.Timestamp,
            ProcessId: raw.ProcessId,
            ParentProcessId: 0,
            ThreadId: raw.ThreadId,
            ProcessImage: "",
            UserSid: null,
            Host: Environment.MachineName,
            ImagePath: fileName,
            ImageBase: imageBase,
            ImageSize: imageSize);
    }
}
