using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Interfaces;

public interface INotificationTemplateRepository
{
    Task<IReadOnlyList<NotificationTemplate>> GetAllAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<NotificationTemplate?> GetByIdAsync(Guid templateId, CancellationToken cancellationToken = default);
    Task<NotificationTemplate?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(string code, Guid? excludingId = null, CancellationToken cancellationToken = default);
    Task AddAsync(NotificationTemplate template, CancellationToken cancellationToken = default);
}
