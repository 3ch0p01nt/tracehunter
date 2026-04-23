using TraceHunter.Core;

namespace TraceHunter.Normalization;

/// <summary>
/// Keyed lookup of <see cref="INormalizedParser"/> by <see cref="ProviderId"/>.
/// Built once from a collection of parsers; for each <see cref="ProviderId"/>
/// value the registry asks every parser <see cref="INormalizedParser.CanParse"/>
/// and the first parser that says yes wins.
/// </summary>
public sealed class ParserRegistry
{
    private readonly Dictionary<ProviderId, INormalizedParser> _parsers;

    public ParserRegistry(IEnumerable<INormalizedParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        _parsers = new Dictionary<ProviderId, INormalizedParser>();
        foreach (var p in parsers)
        {
            foreach (ProviderId id in Enum.GetValues<ProviderId>())
            {
                if (_parsers.ContainsKey(id))
                {
                    continue;
                }
                if (p.CanParse(id))
                {
                    _parsers[id] = p;
                }
            }
        }
    }

    public INormalizedParser? Resolve(ProviderId id) => _parsers.GetValueOrDefault(id);
}
