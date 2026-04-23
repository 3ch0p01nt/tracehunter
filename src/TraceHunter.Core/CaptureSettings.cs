using System.Collections.Frozen;

namespace TraceHunter.Core;

public sealed record CaptureSettings
{
    public bool EnableKernelSession { get; init; } = true;
    public bool EnableUserSession { get; init; } = true;
    public FrozenSet<ProviderId> EnabledProviders { get; init; } = AllProviders;
    public int ChannelCapacity { get; init; } = 100_000;
    public string KernelSessionName { get; init; } = "TraceHunter-Kernel";
    public string UserSessionName { get; init; } = "TraceHunter-User";

    public static FrozenSet<ProviderId> AllProviders { get; } = new[]
    {
        ProviderId.KernelProcess, ProviderId.KernelImage, ProviderId.KernelNetwork,
        ProviderId.PowerShell, ProviderId.DotNetRuntime, ProviderId.DnsClient,
        // WmiActivity intentionally excluded from v1 defaults: enabling its provider GUID
        // (1418ef04-b0b4-4623-bf7e-d74ab47bbdaa) crashes Source.Process() with
        // 0x80071069 (WBEM_E_NOT_FOUND) on some Windows hosts, including
        // GitHub Actions windows-latest runners. Tracked for Phase 1.x investigation.
    }.ToFrozenSet();
}
