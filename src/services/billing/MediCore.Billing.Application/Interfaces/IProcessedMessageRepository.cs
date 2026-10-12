using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Interfaces;

public interface IProcessedMessageRepository
{
    Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task AddAsync(ProcessedMessage message, CancellationToken cancellationToken = default);
}
