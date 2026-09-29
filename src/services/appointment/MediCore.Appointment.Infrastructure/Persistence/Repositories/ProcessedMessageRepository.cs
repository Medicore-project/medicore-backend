using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Appointment.Infrastructure.Persistence.Repositories;

/// <summary>EF Core implementation of <see cref="IProcessedMessageRepository"/>.</summary>
public sealed class ProcessedMessageRepository : IProcessedMessageRepository
{
    private readonly AppointmentDbContext _dbContext;

    public ProcessedMessageRepository(AppointmentDbContext dbContext)
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
