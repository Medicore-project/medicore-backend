using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;

namespace MediCore.Billing.Application.Services;

public sealed class NotificationTemplateService : INotificationTemplateService
{
    private readonly INotificationTemplateRepository _templates;
    private readonly INotificationLogRepository _logs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public NotificationTemplateService(
        INotificationTemplateRepository templates,
        INotificationLogRepository logs,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _templates = templates;
        _logs = logs;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<NotificationTemplateResponse>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        (await _templates.GetAllAsync(includeInactive, cancellationToken)).Select(Map).ToArray();

    public async Task<NotificationTemplateResponse?> GetByIdAsync(
        Guid templateId,
        CancellationToken cancellationToken = default)
    {
        var template = await _templates.GetByIdAsync(templateId, cancellationToken);
        return template is null ? null : Map(template);
    }

    public async Task<CreateNotificationTemplateResult> CreateAsync(
        CreateNotificationTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(request.Code);
        if (await _templates.CodeExistsAsync(code, cancellationToken: cancellationToken))
        {
            return new DuplicateNotificationTemplateCodeResult(code);
        }

        var template = new NotificationTemplate
        {
            Code = code,
            Name = request.Name.Trim(),
            SubjectTemplate = request.SubjectTemplate.Trim(),
            BodyTemplate = request.BodyTemplate.Trim(),
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            IsActive = true
        };
        await _templates.AddAsync(template, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new NotificationTemplateCreatedResult(Map(template));
    }

    public async Task<UpdateNotificationTemplateResult> UpdateAsync(
        Guid templateId,
        UpdateNotificationTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var template = await _templates.GetByIdAsync(templateId, cancellationToken);
        if (template is null)
        {
            return new NotificationTemplateNotFoundResult();
        }

        template.Name = request.Name.Trim();
        template.SubjectTemplate = request.SubjectTemplate.Trim();
        template.BodyTemplate = request.BodyTemplate.Trim();
        template.IsActive = request.IsActive;
        template.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new NotificationTemplateUpdatedResult(Map(template));
    }

    public async Task<bool> DeactivateAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        var template = await _templates.GetByIdAsync(templateId, cancellationToken);
        if (template is null)
        {
            return false;
        }

        template.IsActive = false;
        template.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<NotificationLogResponse>> GetRecentLogsAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        (await _logs.GetRecentAsync(Math.Clamp(limit, 1, 200), cancellationToken))
            .Select(log => new NotificationLogResponse(
                log.NotificationLogId,
                log.SourceMessageId,
                log.EventType,
                log.TemplateCode,
                log.Recipient,
                log.Subject,
                log.Status,
                log.AttemptCount,
                log.Error,
                log.CreatedAtUtc,
                log.LastAttemptAtUtc,
                log.SentAtUtc))
            .ToArray();

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static NotificationTemplateResponse Map(NotificationTemplate template) => new(
        template.NotificationTemplateId,
        template.Code,
        template.Name,
        template.SubjectTemplate,
        template.BodyTemplate,
        template.IsActive,
        template.CreatedAtUtc,
        template.UpdatedAtUtc);
}
