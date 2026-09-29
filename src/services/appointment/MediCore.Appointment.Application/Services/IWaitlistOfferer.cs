using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Services;

/// <summary>
/// Hands released slots to the waitlist (SCRUM-37 AC2) and passes an offer down the queue when
/// the patient holding it lets it go (AC3).
/// </summary>
/// <remarks>
/// Always called inside the caller's transaction, and takes the slot's queue lock itself, so it
/// may be called from any change that frees a slot. Lock order stays appointment row → patient →
/// queue → slot: the queue lock is taken after anything the caller already holds.
/// </remarks>
public interface IWaitlistOfferer
{
    /// <summary>
    /// Offers a slot that has just become <see cref="SlotStatus.Available"/> to the first eligible
    /// waiting entry in its doctor's day, holding the slot as <see cref="SlotStatus.Offered"/>.
    /// Stages the changes and does not save; the caller's one save commits them with whatever
    /// released the slot, so the slot is never publicly bookable in between.
    /// </summary>
    /// <returns>The entry now holding the offer, or null when the slot stays available.</returns>
    Task<WaitlistEntry?> OfferAsync(
        Slot slot,
        DateTime nowUtc,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes an entry that holds an offer — declined, expired or withdrawn — and offers its slot
    /// to the next eligible entry, or returns the slot to the public list when there is none.
    /// </summary>
    /// <remarks>
    /// Saves the closed entry and the freed slot <em>before</em> making the next offer, still inside
    /// the caller's transaction. Both entries hold the same slot id, and the partial unique index on
    /// open offers is checked row by row, so the old offer must be gone from the database before the
    /// new one is written; the order EF would issue two updates in is not something to rely on. The
    /// caller saves once more afterwards, as usual.
    /// </remarks>
    /// <param name="slot">The offered slot, or null if schedule revision has deleted it.</param>
    /// <returns>The entry now holding the offer, or null.</returns>
    Task<WaitlistEntry?> PassOnAsync(
        WaitlistEntry entry,
        Slot? slot,
        string closingStatus,
        string? reason,
        DateTime nowUtc,
        string actor,
        CancellationToken cancellationToken = default);
}
