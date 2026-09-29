namespace MediCore.Appointment.Application.Services;

/// <summary>
/// The waitlist's housekeeping pass (SCRUM-37), run every few seconds in the background: the part
/// of the waitlist that happens because time passes rather than because someone asked.
/// </summary>
public interface IWaitlistSweeper
{
    /// <summary>
    /// One pass, in this order:
    /// <list type="number">
    /// <item>Offers that have lapsed are expired and passed to the next in line — AC3.</item>
    /// <item>Slots held for an offer that no longer exists go back on the public list.</item>
    /// <item>Queues that can no longer be served — the day has passed, the doctor has left, or no
    /// clinic time remains — are closed.</item>
    /// <item>Queues with a free slot on their day are offered it: slots freed by unblocking or by a
    /// schedule change, which, unlike a cancellation, do not offer as they free.</item>
    /// </list>
    /// Each item is its own transaction under its queue's lock, re-read after locking, so a patient
    /// answering at the same moment is never overruled. An item that fails is logged and left for
    /// the next pass.
    /// </summary>
    Task<WaitlistSweepSummary> SweepAsync(CancellationToken cancellationToken = default);
}

/// <summary>What one sweep did.</summary>
public sealed record WaitlistSweepSummary(
    int OffersExpired,
    int HoldsReleased,
    int EntriesClosed,
    int SlotsOffered)
{
    public bool DidAnything => OffersExpired + HoldsReleased + EntriesClosed + SlotsOffered > 0;
}
