using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Interfaces;

public interface INotificationLogRepository
{
    Task<NotificationLog?> GetBySourceAsync(Guid sourceMessageId, string templateCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationLog>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
    Task AddAsync(NotificationLog notification, CancellationToken cancellationToken = default);
}
