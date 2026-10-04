using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Billing.Infrastructure.Persistence.Repositories;

public sealed class ProcessedMessageRepository : IProcessedMessageRepository
{
    private readonly BillingDbContext _dbContext;

    public ProcessedMessageRepository(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        _dbContext.ProcessedMessages.AsNoTracking()
            .AnyAsync(message => message.MessageId == messageId, cancellationToken);

    public Task AddAsync(ProcessedMessage message, CancellationToken cancellationToken = default) =>
        _dbContext.ProcessedMessages.AddAsync(message, cancellationToken).AsTask();
}
