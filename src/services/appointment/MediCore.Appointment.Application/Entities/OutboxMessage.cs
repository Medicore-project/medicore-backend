namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// Transactional outbox row. Created in SCRUM-32 so that SCRUM-34 (booking) can publish
/// appointment events without a second schema migration. Nothing writes to this table yet and
/// no <c>OutboxProcessor</c> is registered.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public required string Topic { get; set; }
    public required string EventKey { get; set; }
    public required string EventType { get; set; }
    public int EventVersion { get; set; } = 1;
    public required string CorrelationId { get; set; }
    public required string Payload { get; set; }
    public DateTime OccurredOnUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }
    public int RetryCount { get; set; }
    public string? Error { get; set; }
}
