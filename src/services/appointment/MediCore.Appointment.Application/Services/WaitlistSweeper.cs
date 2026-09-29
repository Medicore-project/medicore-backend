using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using Microsoft.Extensions.Logging;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IWaitlistSweeper"/>
public sealed class WaitlistSweeper : IWaitlistSweeper
{
    /// <summary>What the sweeper writes into the audit columns and the history.</summary>
    public const string Actor = "waitlist-sweeper";

    /// <summary>The most lapsed offers or orphaned holds handled in one pass; the rest wait for the next.</summary>
    public const int BatchSize = 50;

    public const string DayPassedReason = "The day has passed.";
    public const string DoctorUnavailableReason = "The doctor is no longer taking appointments.";
    public const string NoClinicReason = "No clinic times remain that day.";

    private readonly IWaitlistRepository _waitlistRepository;
    private readonly IWaitlistOfferer _offerer;
    private readonly ISlotRepository _slotRepository;
    private readonly IDoctorCacheRepository _doctorRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WaitlistSweeper> _logger;

    public WaitlistSweeper(
        IWaitlistRepository waitlistRepository,
        IWaitlistOfferer offerer,
        ISlotRepository slotRepository,
        IDoctorCacheRepository doctorRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<WaitlistSweeper> logger)
    {
        _waitlistRepository = waitlistRepository;
        _offerer = offerer;
        _slotRepository = slotRepository;
        _doctorRepository = doctorRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<WaitlistSweepSummary> SweepAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var expired = 0;
        foreach (var lapsed in await _waitlistRepository.GetLapsedOffersAsync(nowUtc, BatchSize, cancellationToken))
        {
            expired += await RunItemAsync(
                "expire offer", lapsed.WaitlistEntryId, token => ExpireOfferAsync(lapsed, nowUtc, token), cancellationToken);
        }

        var released = 0;
        foreach (var orphan in await _waitlistRepository.GetOrphanedOfferedSlotsAsync(BatchSize, cancellationToken))
        {
            released += await RunItemAsync(
                "release hold", orphan.SlotId, token => ReleaseHoldAsync(orphan, nowUtc, token), cancellationToken);
        }

        var closed = 0;
        var offered = 0;
        foreach (var queue in await _waitlistRepository.GetActiveQueuesAsync(cancellationToken))
        {
            var closedHere = await RunItemAsync(
                "close queue", queue.DoctorId, token => CloseIfUnservedAsync(queue, nowUtc, token), cancellationToken);
            closed += closedHere;

            if (closedHere == 0)
            {
                offered += await RunItemAsync(
                    "offer free slots", queue.DoctorId, token => OfferFreeSlotsAsync(queue, nowUtc, token), cancellationToken);
            }
        }

        var summary = new WaitlistSweepSummary(expired, released, closed, offered);
        if (summary.DidAnything)
        {
            _logger.LogInformation(
                "Waitlist sweep: offers expired {Expired}, holds released {Released}, "
                + "entries closed {Closed}, slots offered {Offered}.",
                expired, released, closed, offered);
        }

        return summary;
    }

    /// <summary>AC3: a lapsed offer is closed and the slot passed to the next in line.</summary>
    private async Task<int> ExpireOfferAsync(WaitlistEntry lapsed, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await _waitlistRepository.LockQueueAsync(lapsed.DoctorId, lapsed.SlotDate, cancellationToken);

        // Re-read under the lock: the patient may have accepted or declined a moment ago.
        var entry = await _waitlistRepository.GetTrackedByEntryIdAsync(lapsed.WaitlistEntryId, cancellationToken);
        if (entry is not { Status: WaitlistStatus.Offered } || entry.OfferExpiresAtUtc > nowUtc)
        {
            return 0;
        }

        var slot = entry.OfferedSlotId is { } slotId
            ? await _slotRepository.GetTrackedBySlotIdAsync(slotId, cancellationToken)
            : null;

        await _offerer.PassOnAsync(entry, slot, WaitlistStatus.Expired, null, nowUtc, Actor, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return 1;
    }

    /// <summary>
    /// A slot held for an offer that no longer exists goes back to the queue, or to the public list.
    /// Nothing is meant to leave one behind; this makes sure a slip never hides a time for ever.
    /// </summary>
    private async Task<int> ReleaseHoldAsync(Slot orphan, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await _waitlistRepository.LockQueueAsync(orphan.DoctorId, orphan.SlotDate, cancellationToken);

        var slot = await _slotRepository.GetTrackedBySlotIdAsync(orphan.SlotId, cancellationToken);
        if (slot is not { Status: SlotStatus.Offered }
            || await _waitlistRepository.HasOpenOfferForSlotAsync(slot.SlotId, cancellationToken))
        {
            return 0;
        }

        slot.Status = SlotStatus.Available;
        slot.UpdatedBy = Actor;
        await _offerer.OfferAsync(slot, nowUtc, Actor, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return 1;
    }

    /// <summary>
    /// Closes every active entry in a queue that can no longer be served. Returns how many closed;
    /// zero means the queue is still live.
    /// </summary>
    private async Task<int> CloseIfUnservedAsync(WaitlistQueue queue, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var (status, reason) = await WhyUnservedAsync(queue, nowUtc, cancellationToken);
        if (status is null)
        {
            return 0;
        }

        await _waitlistRepository.LockQueueAsync(queue.DoctorId, queue.SlotDate, cancellationToken);
        var entries = await _waitlistRepository.GetActiveTrackedAsync(queue.DoctorId, queue.SlotDate, cancellationToken);

        foreach (var entry in entries)
        {
            // A hold on a slot that is still in the future goes back to the public list; one in the
            // past is left as it is — nobody can book it either way.
            if (entry.Status == WaitlistStatus.Offered && entry.OfferedSlotId is { } slotId)
            {
                var slot = await _slotRepository.GetTrackedBySlotIdAsync(slotId, cancellationToken);
                if (slot is { Status: SlotStatus.Offered })
                {
                    slot.Status = SlotStatus.Available;
                    slot.UpdatedBy = Actor;
                }
            }

            entry.Status = status;
            entry.ClosedAtUtc = nowUtc;
            entry.ClosedReason = reason;
            entry.UpdatedBy = Actor;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return entries.Count;
    }

    /// <summary>Why a queue cannot be served, or a null status when it still can.</summary>
    private async Task<(string? Status, string? Reason)> WhyUnservedAsync(
        WaitlistQueue queue,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (queue.SlotDate < ColomboTime.ToColomboDate(nowUtc))
        {
            return (WaitlistStatus.Expired, DayPassedReason);
        }

        if (await _doctorRepository.GetActiveAsync(queue.DoctorId, cancellationToken) is null)
        {
            return (WaitlistStatus.Withdrawn, DoctorUnavailableReason);
        }

        // A holiday, approved leave or schedule change removed the day, or today's clinic is over.
        var days = await _waitlistRepository.GetDayAvailabilityAsync(
            queue.DoctorId, queue.SlotDate, queue.SlotDate, nowUtc, cancellationToken);

        return days.Any(day => day.Date == queue.SlotDate)
            ? (null, null)
            : (WaitlistStatus.Withdrawn, NoClinicReason);
    }

    /// <summary>
    /// Offers each free slot on the queue's day to the queue, soonest first, until the free slots
    /// or the eligible patients run out. Saved after each offer, so the next is decided on what the
    /// database now says.
    /// </summary>
    private async Task<int> OfferFreeSlotsAsync(WaitlistQueue queue, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var free = await _slotRepository.GetAvailableAsync(
            queue.DoctorId, queue.SlotDate, queue.SlotDate, nowUtc, cancellationToken);
        if (free.Count == 0)
        {
            return 0;
        }

        var offered = 0;
        foreach (var candidate in free)
        {
            var slot = await _slotRepository.GetTrackedBySlotIdAsync(candidate.SlotId, cancellationToken);
            if (slot is null || await _offerer.OfferAsync(slot, nowUtc, Actor, cancellationToken) is null)
            {
                continue;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            offered++;
        }

        return offered;
    }

    /// <summary>
    /// Runs one item in its own transaction. A failure — most often a lost race with a patient
    /// answering — rolls that item back, is logged, and leaves it for the next pass.
    /// </summary>
    private async Task<int> RunItemAsync(
        string what,
        Guid id,
        Func<CancellationToken, Task<int>> item,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(item, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Waitlist sweep could not {What} for {Id}; it will be retried.", what, id);
            return 0;
        }
    }
}
