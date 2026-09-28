using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>
/// A waitlist entry with what a list needs beside it: the doctor's name from the cache and the
/// offered slot's times. Each is null when its row is missing — the doctor was never cached, or
/// schedule revision deleted the slot.
/// </summary>
public sealed record WaitlistListing(
    WaitlistEntry Entry,
    string? DoctorName,
    string? DoctorSpecialization,
    DateTime? OfferedStartUtc,
    DateTime? OfferedEndUtc);

/// <summary>One doctor's day that has somebody waiting in it, or holding an offer from it.</summary>
public sealed record WaitlistQueue(Guid DoctorId, DateOnly SlotDate);

/// <summary>One waiting entry's place in the queue order, for working out places in line.</summary>
public sealed record WaitingPosition(Guid DoctorId, DateOnly SlotDate, int Position);

/// <summary>
/// How one of a doctor's days stands, counting only slots that have not started.
/// </summary>
/// <param name="Free">Slots anyone could book now.</param>
/// <param name="Taken">Slots booked, or held for the waitlist.</param>
public sealed record DayAvailability(DateOnly Date, int Free, int Taken)
{
    /// <summary>
    /// Full: the clinic runs that day and every remaining time is taken. A day whose slots are all
    /// blocked, or all past, is not full — it has no clinic left to wait for.
    /// </summary>
    public bool IsFull => Taken > 0 && Free == 0;
}

/// <summary>Data-access contract for <see cref="WaitlistEntry"/> (SCRUM-37).</summary>
/// <remarks>
/// A queue is one doctor's day. Every write to a queue first takes <see cref="LockQueueAsync"/>
/// inside the caller's transaction, so joins, offers and their answers on one queue run one at a
/// time and each reads what the one before committed.
/// </remarks>
public interface IWaitlistRepository
{
    /// <summary>
    /// Takes the queue's transaction-scoped advisory lock, waiting for any holder. Only meaningful
    /// inside <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>; released at commit or
    /// rollback. Taking it twice in one transaction is harmless.
    /// </summary>
    Task LockQueueAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// One more than the highest position the queue has ever used — closed and soft-deleted
    /// entries included, so a position is never handed out twice. 1 for an empty queue.
    /// </summary>
    Task<int> GetNextPositionAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Whether the patient is already waiting in, or holding an offer from, the queue.</summary>
    Task<bool> HasActiveEntryAsync(
        Guid patientId,
        Guid doctorId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>How many active entries the patient has for days from <paramref name="from"/> on.</summary>
    Task<int> CountActiveForPatientAsync(
        Guid patientId,
        DateOnly from,
        CancellationToken cancellationToken = default);

    /// <summary>Whether the patient already has a booked appointment with this doctor that day.</summary>
    Task<bool> HasBookedWithDoctorOnAsync(
        Guid patientId,
        Guid doctorId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// For each of the doctor's days in the range that has any bookable, booked or held slot not
    /// yet started, how many are free and how many are taken. Days with none are left out.
    /// </summary>
    Task<IReadOnlyList<DayAvailability>> GetDayAvailabilityAsync(
        Guid doctorId,
        DateOnly from,
        DateOnly to,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The queue's waiting entries, tracked, first in line first — the candidates for an offer.
    /// Call with the queue lock held.
    /// </summary>
    Task<IReadOnlyList<WaitlistEntry>> GetWaitingTrackedAsync(
        Guid doctorId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One entry, untracked — to learn whose it is and which queue to lock before reading it for
    /// real with <see cref="GetTrackedByEntryIdAsync"/>.
    /// </summary>
    Task<WaitlistEntry?> GetByEntryIdAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default);

    /// <summary>One entry, tracked. Call with the queue lock held, so what it reads is current.</summary>
    Task<WaitlistEntry?> GetTrackedByEntryIdAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The queue's active entries — waiting and offered — tracked, in position order. For closing
    /// a queue that can no longer be served. Call with the queue lock held.
    /// </summary>
    Task<IReadOnlyList<WaitlistEntry>> GetActiveTrackedAsync(
        Guid doctorId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Open offers holding any of the given slots, tracked. Schedule revision uses this to put the
    /// patient back in line before it deletes a slot they were offered.
    /// </summary>
    Task<IReadOnlyList<WaitlistEntry>> GetTrackedOfferedForSlotsAsync(
        IReadOnlyCollection<Guid> slotIds,
        CancellationToken cancellationToken = default);

    /// <summary>Open offers whose expiry is at or before <paramref name="nowUtc"/>, oldest first, untracked.</summary>
    Task<IReadOnlyList<WaitlistEntry>> GetLapsedOffersAsync(
        DateTime nowUtc,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Slots held as <see cref="SlotStatus.Offered"/> that no open offer points at, untracked. They
    /// would otherwise stay hidden from the public list for ever.
    /// </summary>
    Task<IReadOnlyList<Slot>> GetOrphanedOfferedSlotsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Whether an open offer holds the slot.</summary>
    Task<bool> HasOpenOfferForSlotAsync(Guid slotId, CancellationToken cancellationToken = default);

    /// <summary>Every queue with an active entry in it.</summary>
    Task<IReadOnlyList<WaitlistQueue>> GetActiveQueuesAsync(CancellationToken cancellationToken = default);

    /// <summary>Stages a new entry for insertion (not yet committed).</summary>
    Task AddAsync(WaitlistEntry entry, CancellationToken cancellationToken = default);

    /// <summary>One entry with its details, untracked; null if there is none.</summary>
    Task<WaitlistListing?> GetListingAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The patient's active entries, and those that closed on or after <paramref name="closedSince"/>,
    /// soonest day first.
    /// </summary>
    Task<IReadOnlyList<WaitlistListing>> ListForPatientAsync(
        Guid patientId,
        DateTime closedSince,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Entries for days in the range, optionally for one doctor and only in the given statuses,
    /// ordered by day, doctor and position.
    /// </summary>
    Task<IReadOnlyList<WaitlistListing>> ListAsync(
        Guid? doctorId,
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<string>? statuses,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The positions of every waiting entry in the given doctors' queues between two days — enough
    /// to work out any of those entries' places in line.
    /// </summary>
    Task<IReadOnlyList<WaitingPosition>> GetWaitingPositionsAsync(
        IReadOnlyCollection<Guid> doctorIds,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
