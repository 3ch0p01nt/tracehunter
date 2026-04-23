using System.Text.Json.Serialization;

namespace TraceHunter.Core;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Process), nameof(Process))]
[JsonDerivedType(typeof(ImageLoad), nameof(ImageLoad))]
[JsonDerivedType(typeof(Network), nameof(Network))]
[JsonDerivedType(typeof(Script), nameof(Script))]
[JsonDerivedType(typeof(Runtime), nameof(Runtime))]
[JsonDerivedType(typeof(Dns), nameof(Dns))]
public abstract record NormalizedEvent(
    DateTimeOffset Timestamp,
    int ProcessId,
    int ParentProcessId,
    int ThreadId,
    string ProcessImage,
    string? UserSid,
    string Host)
{
    public abstract ProviderId ProviderId { get; }

    public sealed record Process(
        DateTimeOffset Timestamp, int ProcessId, int ParentProcessId, int ThreadId,
        string ProcessImage, string? UserSid, string Host,
        ProcessEventKind Kind,
        string CommandLine,
        string ImagePath,
        string? Integrity)
        : NormalizedEvent(Timestamp, ProcessId, ParentProcessId, ThreadId, ProcessImage, UserSid, Host)
    {
        public override ProviderId ProviderId => ProviderId.KernelProcess;
    }

    public sealed record ImageLoad(
        DateTimeOffset Timestamp, int ProcessId, int ParentProcessId, int ThreadId,
        string ProcessImage, string? UserSid, string Host,
        string ImagePath,
        ulong ImageBase,
        int ImageSize)
        : NormalizedEvent(Timestamp, ProcessId, ParentProcessId, ThreadId, ProcessImage, UserSid, Host)
    {
        public override ProviderId ProviderId => ProviderId.KernelImage;
    }

    public sealed record Network(
        DateTimeOffset Timestamp, int ProcessId, int ParentProcessId, int ThreadId,
        string ProcessImage, string? UserSid, string Host,
        NetworkDirection Direction,
        string Protocol,
        string LocalAddress,
        int LocalPort,
        string RemoteAddress,
        int RemotePort)
        : NormalizedEvent(Timestamp, ProcessId, ParentProcessId, ThreadId, ProcessImage, UserSid, Host)
    {
        public override ProviderId ProviderId => ProviderId.KernelNetwork;
    }

    public sealed record Script(
        DateTimeOffset Timestamp, int ProcessId, int ParentProcessId, int ThreadId,
        string ProcessImage, string? UserSid, string Host,
        string ScriptBlockId,
        string Content)
        : NormalizedEvent(Timestamp, ProcessId, ParentProcessId, ThreadId, ProcessImage, UserSid, Host)
    {
        public override ProviderId ProviderId => ProviderId.PowerShell;
    }

    public sealed record Runtime(
        DateTimeOffset Timestamp, int ProcessId, int ParentProcessId, int ThreadId,
        string ProcessImage, string? UserSid, string Host,
        RuntimeEventKind Kind,
        string Detail)
        : NormalizedEvent(Timestamp, ProcessId, ParentProcessId, ThreadId, ProcessImage, UserSid, Host)
    {
        public override ProviderId ProviderId => ProviderId.DotNetRuntime;
    }

    public sealed record Dns(
        DateTimeOffset Timestamp, int ProcessId, int ParentProcessId, int ThreadId,
        string ProcessImage, string? UserSid, string Host,
        DnsEventKind Kind,
        string QueryName,
        string? QueryType,
        string? Result)
        : NormalizedEvent(Timestamp, ProcessId, ParentProcessId, ThreadId, ProcessImage, UserSid, Host)
    {
        public override ProviderId ProviderId => ProviderId.DnsClient;
    }
}

public enum ProcessEventKind { Start, Exit }
public enum NetworkDirection { Inbound, Outbound }
public enum RuntimeEventKind { Exception, Gc, Jit, AssemblyLoad, ThreadPool, Other }
public enum DnsEventKind { Query, Response }
