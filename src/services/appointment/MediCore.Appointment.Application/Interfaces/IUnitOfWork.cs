namespace MediCore.Appointment.Application.Interfaces;

/// <summary>
/// Commits the work staged by the repositories. Repositories never call <c>SaveChanges</c>
/// themselves, so one HTTP request writes in a single transaction.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Commits staged changes, translating database constraint violations into domain exceptions.
    /// </summary>
    /// <exception cref="Exceptions.DuplicateSlotException">
    /// A slot already exists for that doctor and instant — two reconciliations of the same doctor
    /// raced each other.
    /// </exception>
    /// <exception cref="Exceptions.SlotAlreadyBookedException">
    /// An active appointment already exists for that slot.
    /// </exception>
    /// <exception cref="Exceptions.ConcurrentUpdateException">
    /// A slot this save updates or deletes was changed by someone else after it was read.
    /// </exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> inside one explicit database transaction: committed if it
    /// returns, rolled back if it throws. SCRUM-35 — booking needs its reads and its write in the
    /// same transaction so that a lock taken at the start holds until the write commits.
    /// </summary>
    /// <remarks>
    /// Whatever happens, a failure leaves the change tracker empty, so a caller that retries reads
    /// afresh. A deadlock or serialization failure is rethrown as
    /// <see cref="Exceptions.ConcurrentUpdateException"/>: like a stale concurrency token, it means
    /// another writer got in the way and the whole operation should simply run again.
    /// </remarks>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}
