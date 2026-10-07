using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Billing.Infrastructure.Persistence.Repositories;

public sealed class OutboxMessageRepository : IOutboxMessageRepository
{
    private readonly BillingDbContext _dbContext;

    public OutboxMessageRepository(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
        _dbContext.OutboxMessages.AddAsync(message, cancellationToken).AsTask();

    public async Task<IReadOnlyList<OutboxMessage>> GetUnprocessedBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default) =>
        await _dbContext.OutboxMessages
            .Where(message => message.ProcessedOnUtc == null)
            .OrderBy(message => message.OccurredOnUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
