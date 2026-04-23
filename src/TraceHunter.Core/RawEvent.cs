namespace TraceHunter.Core;

public sealed record RawEvent(
    ProviderId ProviderId,
    int EventId,
    DateTimeOffset Timestamp,
    int ProcessId,
    int ThreadId,
    string PayloadJson)
{
    public ProviderId ProviderId { get; } = ProviderId;
    public int ProcessId { get; } = ProcessId >= 0
        ? ProcessId
        : throw new ArgumentOutOfRangeException(nameof(ProcessId), "Process ID must be non-negative.");
    public string PayloadJson { get; } = PayloadJson ?? throw new ArgumentNullException(nameof(PayloadJson));
}
