namespace MediCore.Contracts.Events;

public abstract record IntegrationEvent
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public string CorrelationId { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
    public abstract string EventType { get; }
    public int Version { get; init; } = 1;
}
