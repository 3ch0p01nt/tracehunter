using TraceHunter.Core;

namespace TraceHunter.Normalization;

public interface INormalizedParser
{
    bool CanParse(ProviderId providerId);
    NormalizedEvent? Parse(RawEvent raw);
}
