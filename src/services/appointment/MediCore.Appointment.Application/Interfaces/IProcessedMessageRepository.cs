using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>Data-access contract for <see cref="ProcessedMessage"/>, the consumer dedupe log.</summary>
public interface IProcessedMessageRepository
{
    Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>Stages a record for insertion (not yet committed).</summary>
    Task AddAsync(ProcessedMessage message, CancellationToken cancellationToken = default);
}
