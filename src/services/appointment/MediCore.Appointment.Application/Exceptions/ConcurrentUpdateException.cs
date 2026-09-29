namespace MediCore.Appointment.Application.Exceptions;

/// <summary>
/// Thrown when a save finds that a row it read has been changed by someone else since — the
/// optimistic concurrency token on <see cref="Entities.Slot"/> no longer matches.
/// </summary>
/// <remarks>
/// SCRUM-35. Nothing was committed and the change tracker has been cleared, so the caller holds no
/// stale entities. What to do next is the caller's decision: booking re-reads the slot and decides
/// again, reconciliation re-plans, and blocking reports the slot's new state.
/// </remarks>
public sealed class ConcurrentUpdateException : Exception
{
    public ConcurrentUpdateException(Exception? innerException = null)
        : base(
            "This record was changed by someone else while you were working on it. Please try again.",
            innerException)
    {
    }
}
