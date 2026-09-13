namespace MediCore.Patient.Application.Entities;

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
