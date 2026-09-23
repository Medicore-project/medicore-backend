using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Appointment.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IOutboxMessageRepository"/>
public sealed class OutboxMessageRepository : IOutboxMessageRepository
{
    private readonly AppointmentDbContext _dbContext;

    public OutboxMessageRepository(AppointmentDbContext dbContext)
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

    // Straight to the context, not through IUnitOfWork — see the remarks on the interface.
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
