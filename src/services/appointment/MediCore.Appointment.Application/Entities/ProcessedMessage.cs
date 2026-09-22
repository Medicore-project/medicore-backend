namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// Record of one consumed Kafka message, keyed by the event's own <c>MessageId</c>. Written in
/// the same transaction as the effect of handling it, so a redelivered message is recognised and
/// skipped rather than applied twice.
/// </summary>
public sealed class ProcessedMessage
{
    public Guid MessageId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ConsumerGroup { get; set; } = string.Empty;
    public string SourceTopic { get; set; } = string.Empty;

    /// <summary>What handling concluded — one of <see cref="ProcessedMessageOutcome"/>.</summary>
    public string Outcome { get; set; } = string.Empty;

    public DateTime ProcessedAtUtc { get; set; }
}

/// <summary>Allowed values for <see cref="ProcessedMessage.Outcome"/>.</summary>
public static class ProcessedMessageOutcome
{
    /// <summary>The message changed state.</summary>
    public const string Applied = "Applied";

    /// <summary>Valid, but nothing to do — e.g. an event about a non-doctor.</summary>
    public const string Ignored = "Ignored";

    /// <summary>Older than state already applied, so skipped.</summary>
    public const string Stale = "Stale";

    /// <summary>Malformed or unsupported; skipped so it cannot block the partition.</summary>
    public const string Rejected = "Rejected";
}
