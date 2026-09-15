using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Patient.Infrastructure.Persistence.Repositories;

public sealed class ProcessedMessageRepository : IProcessedMessageRepository
{
    private readonly PatientDbContext _dbContext;

    public ProcessedMessageRepository(PatientDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        _dbContext.ProcessedMessages
            .AsNoTracking()
            .AnyAsync(message => message.MessageId == messageId, cancellationToken);

    public Task AddAsync(ProcessedMessage message, CancellationToken cancellationToken = default) =>
        _dbContext.ProcessedMessages.AddAsync(message, cancellationToken).AsTask();
}
