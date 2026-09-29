using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>
/// Reads and writes the transactional outbox.
/// </summary>
/// <remarks>
/// <see cref="SaveChangesAsync"/> deliberately goes straight to the <c>DbContext</c> rather than
/// through <see cref="IUnitOfWork"/>. The unit of work clears the change tracker before throwing a
/// translated constraint violation, which is right for a request that is about to end but wrong
/// inside the dispatcher's loop: it would discard the <c>ProcessedOnUtc</c> stamps of every other
/// message in the batch that <em>did</em> publish, and they would all be published again on the
/// next tick. None of the translated constraints can be violated by a batch that only writes
/// <c>ProcessedOnUtc</c>, <c>RetryCount</c> and <c>Error</c> on existing rows anyway.
/// <para>
/// Writing a row is the opposite case: the booking service stages the appointment, the slot
/// mutation and the outbox row together and commits them through <see cref="IUnitOfWork"/> in one
/// save, because that single transaction is what makes the booking and its event atomic.
/// </para>
/// </remarks>
public interface IOutboxMessageRepository
{
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxMessage>> GetUnprocessedBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
