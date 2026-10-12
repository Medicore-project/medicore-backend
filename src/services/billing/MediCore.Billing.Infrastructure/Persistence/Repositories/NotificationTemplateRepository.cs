using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Billing.Infrastructure.Persistence.Repositories;

public sealed class NotificationTemplateRepository : INotificationTemplateRepository
{
    private readonly BillingDbContext _dbContext;

    public NotificationTemplateRepository(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<NotificationTemplate>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.NotificationTemplates.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(template => template.IsActive);
        }

        return await query.OrderBy(template => template.Code).ToListAsync(cancellationToken);
    }

    public Task<NotificationTemplate?> GetByIdAsync(
        Guid templateId,
        CancellationToken cancellationToken = default) =>
        _dbContext.NotificationTemplates.FirstOrDefaultAsync(
            template => template.NotificationTemplateId == templateId,
            cancellationToken);

    public Task<NotificationTemplate?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default) =>
        _dbContext.NotificationTemplates.AsNoTracking().FirstOrDefaultAsync(
            template => template.Code == code,
            cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default) =>
        _dbContext.NotificationTemplates.AnyAsync(
            template => template.Code == code
                && (!excludingId.HasValue || template.NotificationTemplateId != excludingId.Value),
            cancellationToken);

    public Task AddAsync(NotificationTemplate template, CancellationToken cancellationToken = default) =>
        _dbContext.NotificationTemplates.AddAsync(template, cancellationToken).AsTask();
}
