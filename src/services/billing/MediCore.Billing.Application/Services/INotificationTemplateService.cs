using MediCore.Billing.Application.DTOs;

namespace MediCore.Billing.Application.Services;

public interface INotificationTemplateService
{
    Task<IReadOnlyList<NotificationTemplateResponse>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<NotificationTemplateResponse?> GetByIdAsync(Guid templateId, CancellationToken cancellationToken = default);
    Task<CreateNotificationTemplateResult> CreateAsync(CreateNotificationTemplateRequest request, CancellationToken cancellationToken = default);
    Task<UpdateNotificationTemplateResult> UpdateAsync(Guid templateId, UpdateNotificationTemplateRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeactivateAsync(Guid templateId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationLogResponse>> GetRecentLogsAsync(int limit, CancellationToken cancellationToken = default);
}

public abstract record CreateNotificationTemplateResult;
public sealed record NotificationTemplateCreatedResult(NotificationTemplateResponse Template) : CreateNotificationTemplateResult;
public sealed record DuplicateNotificationTemplateCodeResult(string Code) : CreateNotificationTemplateResult;

public abstract record UpdateNotificationTemplateResult;
public sealed record NotificationTemplateUpdatedResult(NotificationTemplateResponse Template) : UpdateNotificationTemplateResult;
public sealed record NotificationTemplateNotFoundResult : UpdateNotificationTemplateResult;
