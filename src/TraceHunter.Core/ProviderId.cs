namespace TraceHunter.Core;

// Numeric values pinned. Storage in v1 (Phase 4 SQLite) and JSON sinks
// will persist these as integers. Add new providers with explicit values
// at the end; never reuse retired values.
public enum ProviderId
{
    Unknown = 0,
    KernelProcess = 1,
    KernelImage = 2,
    KernelNetwork = 3,
    PowerShell = 4,
    DotNetRuntime = 5,
    DnsClient = 6,
    WmiActivity = 7,
    KernelFileIo = 8,    // reserved (v1.2)
    KernelRegistry = 9,  // reserved (v1.2)
    Test = 99,           // reserved for synthetic test EventSources
}
