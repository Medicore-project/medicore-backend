namespace MediCore.Identity.Application.Entities;

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Topic { get; set; }
    public required string EventType { get; set; }

    public required string Payload { get; set; }

    public DateTime OccurredOnUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedOnUtc { get; set; }

    public int RetryCount { get; set; }

    public string? Error { get; set; }
}