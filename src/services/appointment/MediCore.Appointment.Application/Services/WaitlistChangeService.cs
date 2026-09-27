using MediCore.Appointment.Application.Concurrency;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IWaitlistChangeService"/>
/// <remarks>
/// Every answer runs as a bounded series of attempts, each in its own transaction, the same shape
/// as booking and appointment changes. Each attempt reads the entry once untracked to learn whose
/// it is and which queue it is in, takes the locks, and only then reads it tracked, so what it
/// decides on is what the last writer to that queue committed. Lock order: patient → queue → slot.
/// </remarks>
public sealed class WaitlistChangeService : IWaitlistChangeService
{
    private readonly IWaitlistRepository _waitlistRepository;
    private readonly IWaitlistOfferer _offerer;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ISlotRepository _slotRepository;
    private readonly IDoctorCacheRepository _doctorRepository;
    private readonly IOutboxMessageRepository _outboxRepository;
    private readonly IAppointmentHistoryRepository _historyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public WaitlistChangeService(
        IWaitlistRepository waitlistRepository,
        IWaitlistOfferer offerer,
        IAppointmentRepository appointmentRepository,
        ISlotRepository slotRepository,
        IDoctorCacheRepository doctorRepository,
        IOutboxMessageRepository outboxRepository,
        IAppointmentHistoryRepository historyRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _waitlistRepository = waitlistRepository;
        _offerer = offerer;
        _appointmentRepository = appointmentRepository;
        _slotRepository = slotRepository;
        _doctorRepository = doctorRepository;
        _outboxRepository = outboxRepository;
        _historyRepository = historyRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public Task<WaitlistChangeResult> AcceptAsync(
        Guid waitlistEntryId,
        AppointmentCaller caller,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        RunAsync(token => AttemptAcceptAsync(waitlistEntryId, caller, correlationId, token), cancellationToken);

    private async Task<WaitlistChangeResult> AttemptAcceptAsync(
        Guid waitlistEntryId,
        AppointmentCaller caller,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var located = await LocateAsync(waitlistEntryId, caller, cancellationToken);
        if (located is null)
        {
            return new WaitlistEntryNotFoundResult();
        }

        // The patient first, as booking takes it: their bookings and this acceptance run one at a
        // time, so the overlap check below sees any appointment they made a moment ago.
        await _appointmentRepository.LockPatientAsync(located.PatientId, cancellationToken);
        var entry = await LockAndReadAsync(located, cancellationToken);
        if (entry is null)
        {
            return new WaitlistEntryNotFoundResult();
        }

        if (entry.Status != WaitlistStatus.Offered)
        {
            return new WaitlistInvalidStateResult(entry.Status, WaitlistAction.Accept);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // AC3 — inclusive, so the instant of expiry is already too late, and enforced here rather
        // than trusted to the sweeper, which may not have run yet.
        if (entry.OfferExpiresAtUtc is not { } expiresAtUtc || nowUtc >= expiresAtUtc)
        {
            return new WaitlistOfferExpiredResult(entry.OfferExpiresAtUtc ?? nowUtc);
        }

        if (await _doctorRepository.GetActiveAsync(entry.DoctorId, cancellationToken) is null)
        {
            return new WaitlistOfferDoctorNotFoundResult();
        }

        var slot = entry.OfferedSlotId is { } slotId
            ? await _slotRepository.GetTrackedBySlotIdAsync(slotId, cancellationToken)
            : null;

        if (slot is null || slot.Status != SlotStatus.Offered || slot.StartUtc <= nowUtc)
        {
            return new WaitlistOfferedSlotUnavailableResult();
        }

        var clash = await _appointmentRepository.FindPatientOverlapAsync(
            entry.PatientId, slot.StartUtc, slot.EndUtc, cancellationToken: cancellationToken);

        if (clash is not null)
        {
            return new WaitlistPatientOverlapResult(clash.AppointmentId, clash.StartUtc, clash.EndUtc);
        }

        // AC4. The same write as a booking, so downstream cannot tell the two apart. The patient's
        // details come from the entry, not the caller: a receptionist accepting on the phone has
        // a staff token with no patient claims.
        var appointment = await AppointmentCreation.CreateAsync(
            slot,
            entry.PatientId,
            new BookingPatientDetails(entry.PatientNumber, entry.PatientName),
            entry.ServiceCode,
            caller.Actor,
            correlationId,
            nowUtc,
            _appointmentRepository,
            _outboxRepository,
            _historyRepository,
            cancellationToken);

        entry.Status = WaitlistStatus.Accepted;
        entry.AppointmentId = appointment.AppointmentId;
        entry.ClosedAtUtc = nowUtc;
        entry.UpdatedBy = caller.Actor;

        // One save: the slot taken, the appointment, its event and history, and the entry cleared.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new WaitlistAcceptedResult(AppointmentMapping.ToResponse(appointment), entry.WaitlistEntryId);
    }

    public Task<WaitlistChangeResult> DeclineAsync(
        Guid waitlistEntryId,
        AppointmentCaller caller,
        CancellationToken cancellationToken = default) =>
        RunAsync(token => AttemptDeclineAsync(waitlistEntryId, caller, token), cancellationToken);

    private async Task<WaitlistChangeResult> AttemptDeclineAsync(
        Guid waitlistEntryId,
        AppointmentCaller caller,
        CancellationToken cancellationToken)
    {
        var located = await LocateAsync(waitlistEntryId, caller, cancellationToken);
        var entry = located is null ? null : await LockAndReadAsync(located, cancellationToken);
        if (entry is null)
        {
            return new WaitlistEntryNotFoundResult();
        }

        if (entry.Status != WaitlistStatus.Offered)
        {
            return new WaitlistInvalidStateResult(entry.Status, WaitlistAction.Decline);
        }

        await PassOnAsync(entry, WaitlistStatus.Declined, reason: null, caller.Actor, cancellationToken);

        return await ChangedAsync(entry, cancellationToken);
    }

    public Task<WaitlistChangeResult> WithdrawAsync(
        Guid waitlistEntryId,
        AppointmentCaller caller,
        string? reason,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            token => AttemptWithdrawAsync(waitlistEntryId, caller, NullIfBlank(reason), token),
            cancellationToken);

    private async Task<WaitlistChangeResult> AttemptWithdrawAsync(
        Guid waitlistEntryId,
        AppointmentCaller caller,
        string? reason,
        CancellationToken cancellationToken)
    {
        var located = await LocateAsync(waitlistEntryId, caller, cancellationToken);
        var entry = located is null ? null : await LockAndReadAsync(located, cancellationToken);
        if (entry is null)
        {
            return new WaitlistEntryNotFoundResult();
        }

        switch (entry.Status)
        {
            case WaitlistStatus.Offered:
                await PassOnAsync(entry, WaitlistStatus.Withdrawn, reason, caller.Actor, cancellationToken);
                break;

            case WaitlistStatus.Waiting:
                entry.Status = WaitlistStatus.Withdrawn;
                entry.ClosedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
                entry.ClosedReason = reason;
                entry.UpdatedBy = caller.Actor;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                break;

            default:
                return new WaitlistInvalidStateResult(entry.Status, WaitlistAction.Withdraw);
        }

        return await ChangedAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Closes an entry holding an offer and hands its slot to the next in line. The offerer saves
    /// the closed entry before the next offer; this saves the next offer.
    /// </summary>
    private async Task PassOnAsync(
        WaitlistEntry entry,
        string closingStatus,
        string? reason,
        string actor,
        CancellationToken cancellationToken)
    {
        var slot = entry.OfferedSlotId is { } slotId
            ? await _slotRepository.GetTrackedBySlotIdAsync(slotId, cancellationToken)
            : null;

        await _offerer.PassOnAsync(
            entry, slot, closingStatus, reason, _timeProvider.GetUtcNow().UtcDateTime, actor, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The entry, untracked, or null when it does not exist or — for a patient caller — is not
    /// theirs. Only its patient and queue are used; nothing is decided on it.
    /// </summary>
    private async Task<WaitlistEntry?> LocateAsync(
        Guid waitlistEntryId,
        AppointmentCaller caller,
        CancellationToken cancellationToken)
    {
        var entry = await _waitlistRepository.GetByEntryIdAsync(waitlistEntryId, cancellationToken);

        return entry is not null && (caller.PatientId is null || caller.PatientId == entry.PatientId)
            ? entry
            : null;
    }

    /// <summary>Takes the entry's queue lock, then reads the entry as it now is.</summary>
    private async Task<WaitlistEntry?> LockAndReadAsync(WaitlistEntry located, CancellationToken cancellationToken)
    {
        await _waitlistRepository.LockQueueAsync(located.DoctorId, located.SlotDate, cancellationToken);
        return await _waitlistRepository.GetTrackedByEntryIdAsync(located.WaitlistEntryId, cancellationToken);
    }

    /// <summary>The closed entry as the API returns it. Closed entries have no place in line.</summary>
    private async Task<WaitlistChangeResult> ChangedAsync(WaitlistEntry entry, CancellationToken cancellationToken)
    {
        var listing = await _waitlistRepository.GetListingAsync(entry.WaitlistEntryId, cancellationToken)
            ?? new WaitlistListing(entry, null, null, null, null);

        return new WaitlistEntryChangedResult(WaitlistMapping.ToResponse(listing, placeInLine: null));
    }

    private async Task<WaitlistChangeResult> RunAsync(
        Func<CancellationToken, Task<WaitlistChangeResult>> attempt,
        CancellationToken cancellationToken)
    {
        for (var attemptNumber = 1; ; attemptNumber++)
        {
            try
            {
                return await _unitOfWork.ExecuteInTransactionAsync(attempt, cancellationToken);
            }
            catch (SlotAlreadyBookedException)
            {
                // ux_appointments_slot: an appointment already holds the offered slot. The slot
                // was Offered under our lock, so this means something wrote around the lock;
                // refuse rather than guess.
                return new WaitlistOfferedSlotUnavailableResult();
            }
            catch (ConcurrentUpdateException) when (attemptNumber < ConcurrencyRetry.MaxAttempts)
            {
                // Rolled back and the tracker cleared; read everything again.
            }
            catch (ConcurrentUpdateException)
            {
                return new WaitlistChangeContendedResult();
            }
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
