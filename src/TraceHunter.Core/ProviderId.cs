namespace TraceHunter.Core;

public enum ProviderId
{
    Unknown = 0,
    KernelProcess,
    KernelImage,
    KernelNetwork,
    PowerShell,
    DotNetRuntime,
    DnsClient,
    WmiActivity,
}
