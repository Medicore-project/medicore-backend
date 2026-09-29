using MediCore.Appointment.Application.Exceptions;

namespace MediCore.Appointment.Application.Concurrency;

/// <summary>
/// Re-runs a read-decide-write operation when its save loses an optimistic concurrency race.
/// </summary>
/// <remarks>
/// <para>
/// SCRUM-35. A <see cref="ConcurrentUpdateException"/> means another writer committed to a slot
/// between our read and our save, and the unit of work has already cleared the change tracker. The
/// operation is then simply run again: it re-reads the slot as it now is and makes a fresh decision,
/// which is how a blocked-while-you-booked race turns into a correct 409 instead of an error.
/// </para>
/// <para>
/// The operation must be the whole read-decide-write, never just the save. Re-saving without
/// re-reading would either repeat the conflict or overwrite the winner's change.
/// </para>
/// <para>
/// After <see cref="MaxAttempts"/> conflicts in a row the exception is allowed to escape; the API
/// maps it to 409. Three is plenty: each retry needs a different writer to commit to the same row
/// in the few milliseconds between our read and our save.
/// </para>
/// </remarks>
public static class ConcurrencyRetry
{
    /// <summary>The first attempt plus two retries.</summary>
    public const int MaxAttempts = 3;

    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (ConcurrentUpdateException) when (attempt < MaxAttempts)
            {
                // Nothing committed and nothing is tracked; go round and read again.
            }
        }
    }
}
