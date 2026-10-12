namespace MediCore.Billing.Application.Entities;

public sealed class NotificationLog
{
    public Guid NotificationLogId { get; set; } = Guid.NewGuid();
    public Guid NotificationTemplateId { get; set; }
    public Guid SourceMessageId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Status { get; set; } = NotificationStatuses.Pending;
    public int AttemptCount { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? Error { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }

    public NotificationTemplate? Template { get; set; }
}

public static class NotificationStatuses
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}
