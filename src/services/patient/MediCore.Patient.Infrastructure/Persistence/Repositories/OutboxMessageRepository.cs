using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Patient.Infrastructure.Persistence.Repositories;

public sealed class OutboxMessageRepository : IOutboxMessageRepository
{
    private readonly PatientDbContext _dbContext;

    public OutboxMessageRepository(PatientDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        return _dbContext.OutboxMessages.AddAsync(message, cancellationToken).AsTask();
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetUnprocessedBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.OutboxMessages
            .Where(message => message.ProcessedOnUtc == null)
            .OrderBy(message => message.OccurredOnUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
