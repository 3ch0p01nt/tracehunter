namespace TraceHunter.Core;

public sealed record CaptureSettings
{
    public bool EnableKernelSession { get; init; } = true;
    public bool EnableUserSession { get; init; } = true;
    public IReadOnlySet<ProviderId> EnabledProviders { get; init; } = AllProviders;
    public int ChannelCapacity { get; init; } = 100_000;
    public string KernelSessionName { get; init; } = "TraceHunter-Kernel";
    public string UserSessionName { get; init; } = "TraceHunter-User";

    public static IReadOnlySet<ProviderId> AllProviders { get; } = new HashSet<ProviderId>
    {
        ProviderId.KernelProcess, ProviderId.KernelImage, ProviderId.KernelNetwork,
        ProviderId.PowerShell, ProviderId.DotNetRuntime, ProviderId.DnsClient, ProviderId.WmiActivity,
    };
}
