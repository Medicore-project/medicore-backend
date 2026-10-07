using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Interfaces;

public interface IOutboxMessageRepository
{
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboxMessage>> GetUnprocessedBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
