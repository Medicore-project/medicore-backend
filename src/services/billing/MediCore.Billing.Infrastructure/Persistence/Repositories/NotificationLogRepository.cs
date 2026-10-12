using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Billing.Infrastructure.Persistence.Repositories;

public sealed class NotificationLogRepository : INotificationLogRepository
{
    private readonly BillingDbContext _dbContext;

    public NotificationLogRepository(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<NotificationLog?> GetBySourceAsync(
        Guid sourceMessageId,
        string templateCode,
        CancellationToken cancellationToken = default) =>
        _dbContext.NotificationLogs.FirstOrDefaultAsync(
            log => log.SourceMessageId == sourceMessageId && log.TemplateCode == templateCode,
            cancellationToken);

    public async Task<IReadOnlyList<NotificationLog>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        await _dbContext.NotificationLogs.AsNoTracking()
            .OrderByDescending(log => log.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task AddAsync(NotificationLog notification, CancellationToken cancellationToken = default) =>
        _dbContext.NotificationLogs.AddAsync(notification, cancellationToken).AsTask();
}
