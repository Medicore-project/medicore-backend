namespace MediCore.Billing.Application.DTOs;

public sealed record CreateNotificationTemplateRequest(
    string Code,
    string Name,
    string SubjectTemplate,
    string BodyTemplate);

public sealed record UpdateNotificationTemplateRequest(
    string Name,
    string SubjectTemplate,
    string BodyTemplate,
    bool IsActive);

public sealed record NotificationTemplateResponse(
    Guid NotificationTemplateId,
    string Code,
    string Name,
    string SubjectTemplate,
    string BodyTemplate,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record NotificationLogResponse(
    Guid NotificationLogId,
    Guid SourceMessageId,
    string EventType,
    string TemplateCode,
    string Recipient,
    string Subject,
    string Status,
    int AttemptCount,
    string? Error,
    DateTime CreatedAtUtc,
    DateTime? LastAttemptAtUtc,
    DateTime? SentAtUtc);
