using MediCore.Patient.Application.Entities;

namespace MediCore.Patient.Application.Interfaces;

public interface IProcessedMessageRepository
{
    Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task AddAsync(ProcessedMessage message, CancellationToken cancellationToken = default);
}
