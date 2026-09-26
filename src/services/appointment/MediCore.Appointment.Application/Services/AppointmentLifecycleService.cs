using MediCore.Appointment.Application.Concurrency;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Messaging;
using MediCore.Appointment.Application.Scheduling;
using Microsoft.Extensions.Options;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IAppointmentLifecycleService"/>
/// <remarks>
/// Like booking, there is no publisher among these dependencies: every event is an outbox row
/// committed with the change it announces.
/// </remarks>
public sealed class AppointmentLifecycleService : IAppointmentLifecycleService
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ISlotRepository _slotRepository;
    private readonly IDoctorCacheRepository _doctorRepository;
    private readonly IOutboxMessageRepository _outboxRepository;
    private readonly IAppointmentHistoryRepository _historyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationPolicyOptions _cancellationPolicy;

    public AppointmentLifecycleService(
        IAppointmentRepository appointmentRepository,
        ISlotRepository slotRepository,
        IDoctorCacheRepository doctorRepository,
        IOutboxMessageRepository outboxRepository,
        IAppointmentHistoryRepository historyRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IOptions<CancellationPolicyOptions> cancellationPolicy)
    {
        _appointmentRepository = appointmentRepository;
        _slotRepository = slotRepository;
        _doctorRepository = doctorRepository;
        _outboxRepository = outboxRepository;
        _historyRepository = historyRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _cancellationPolicy = cancellationPolicy.Value;
    }

    private int WindowHours => Math.Max(0, _cancellationPolicy.WindowHours);

    public Task<AppointmentChangeResult> CancelAsync(
        Guid appointmentId,
        string reason,
        AppointmentCaller caller,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            token => AttemptCancelAsync(appointmentId, reason.Trim(), caller, correlationId, token),
            cancellationToken);

    private async Task<AppointmentChangeResult> AttemptCancelAsync(
        Guid appointmentId,
        string reason,
        AppointmentCaller caller,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var appointment = await LockAppointmentAsync(appointmentId, caller, cancellationToken);
        if (appointment is null)
        {
            return new AppointmentNotFoundResult();
        }

        if (!AppointmentStatusTransitions.CanTransition(appointment.Status, AppointmentStatus.Cancelled))
        {
            return new AppointmentInvalidTransitionResult(appointment.Status, AppointmentHistoryAction.Cancelled);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        if (IsInsideWindow(appointment, nowUtc))
        {
            return new AppointmentInsideCancellationWindowResult(WindowHours, appointment.StartUtc);
        }

        ReleaseSlot(
            await _slotRepository.GetTrackedBySlotIdAsync(appointment.SlotId, cancellationToken),
            caller.Actor);

        var fromStatus = appointment.Status;
        appointment.Status = AppointmentStatus.Cancelled;
        appointment.UpdatedBy = caller.Actor;

        await _historyRepository.AddAsync(
            new AppointmentHistoryEntry
            {
                AppointmentId = appointment.AppointmentId,
                Action = AppointmentHistoryAction.Cancelled,
                FromStatus = fromStatus,
                ToStatus = appointment.Status,
                FromSlotId = appointment.SlotId,
                FromStartUtc = appointment.StartUtc,
                Reason = reason,
                Actor = caller.Actor,
                OccurredAtUtc = nowUtc
            },
            cancellationToken);
        await _outboxRepository.AddAsync(
            AppointmentOutboxMessages.Cancelled(appointment, reason, correlationId, nowUtc),
            cancellationToken);

        // One save: the appointment, the released slot, the history entry and the event row
        // commit together or not at all.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AppointmentChangedResult(AppointmentMapping.ToResponse(appointment));
    }

    public Task<AppointmentChangeResult> RescheduleAsync(
        Guid appointmentId,
        Guid newSlotId,
        AppointmentCaller caller,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            token => AttemptRescheduleAsync(appointmentId, newSlotId, caller, token),
            cancellationToken);

    /// <summary>
    /// One read-decide-write of a reschedule. The old slot is released and the new one taken in
    /// the same save as the appointment's move, so there is never a moment when the appointment
    /// holds both slots or neither.
    /// </summary>
    private async Task<AppointmentChangeResult> AttemptRescheduleAsync(
        Guid appointmentId,
        Guid newSlotId,
        AppointmentCaller caller,
        CancellationToken cancellationToken)
    {
        var appointment = await LockAppointmentAsync(appointmentId, caller, cancellationToken);
        if (appointment is null)
        {
            return new AppointmentNotFoundResult();
        }

        // Booked → Booked: a reschedule keeps the status and changes the time.
        if (!AppointmentStatusTransitions.CanTransition(appointment.Status, AppointmentStatus.Booked))
        {
            return new AppointmentInvalidTransitionResult(appointment.Status, AppointmentHistoryAction.Rescheduled);
        }

        if (appointment.SlotId == newSlotId)
        {
            return new AppointmentNewSlotSameAsCurrentResult();
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var oldSlot = await _slotRepository.GetTrackedBySlotIdAsync(appointment.SlotId, cancellationToken);

        // A stranded booking is exempt: a schedule change flagged its slot, so the clinic owes the
        // patient a new time however close the old one is.
        var stranded = oldSlot?.Status == SlotStatus.Flagged;
        if (!stranded && IsInsideWindow(appointment, nowUtc))
        {
            return new AppointmentInsideCancellationWindowResult(WindowHours, appointment.StartUtc);
        }

        // As booking does (SCRUM-35): held until the commit, so a booking for the same patient
        // cannot slip into the new time between the overlap check and the save.
        await _appointmentRepository.LockPatientAsync(appointment.PatientId, cancellationToken);

        var newSlot = await _slotRepository.GetTrackedBySlotIdAsync(newSlotId, cancellationToken);
        if (newSlot is null)
        {
            return new AppointmentNewSlotNotFoundResult();
        }

        if (newSlot.DoctorId != appointment.DoctorId)
        {
            return new AppointmentNewSlotDifferentDoctorResult();
        }

        if (await _doctorRepository.GetActiveAsync(newSlot.DoctorId, cancellationToken) is null)
        {
            return new AppointmentDoctorNotFoundResult();
        }

        // Status before time, exactly as booking orders them.
        if (!string.Equals(newSlot.Status, SlotStatus.Available, StringComparison.Ordinal))
        {
            return new AppointmentNewSlotNotAvailableResult(newSlot.Status);
        }

        if (newSlot.StartUtc <= nowUtc)
        {
            return new AppointmentNewSlotInPastResult(newSlot.StartUtc);
        }

        var clash = await _appointmentRepository.FindPatientOverlapAsync(
            appointment.PatientId,
            newSlot.StartUtc,
            newSlot.EndUtc,
            excludeAppointmentId: appointment.AppointmentId,
            cancellationToken: cancellationToken);

        if (clash is not null)
        {
            return new AppointmentPatientOverlapResult(clash.AppointmentId, clash.StartUtc, clash.EndUtc);
        }

        var entry = new AppointmentHistoryEntry
        {
            AppointmentId = appointment.AppointmentId,
            Action = AppointmentHistoryAction.Rescheduled,
            FromStatus = appointment.Status,
            ToStatus = appointment.Status,
            FromSlotId = appointment.SlotId,
            ToSlotId = newSlot.SlotId,
            FromStartUtc = appointment.StartUtc,
            ToStartUtc = newSlot.StartUtc,
            Actor = caller.Actor,
            OccurredAtUtc = nowUtc
        };

        ReleaseSlot(oldSlot, caller.Actor);

        newSlot.Status = SlotStatus.Booked;
        newSlot.UpdatedBy = caller.Actor;

        appointment.SlotId = newSlot.SlotId;
        appointment.StartUtc = newSlot.StartUtc;
        appointment.EndUtc = newSlot.EndUtc;
        appointment.SlotDate = newSlot.SlotDate;
        appointment.DurationMinutes = newSlot.DurationMinutes;
        appointment.UpdatedBy = caller.Actor;

        await _historyRepository.AddAsync(entry, cancellationToken);

        // One save for both slots, the appointment and the history entry. If the new slot was
        // taken in the meantime, its token or ux_appointments_slot throws out of here, the
        // transaction rolls back, and the appointment is still on its original slot (AC1).
        // No event: nothing downstream consumes a reschedule yet, and adding one is a contract
        // change for every team (see the SCRUM-36 decisions).
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AppointmentChangedResult(AppointmentMapping.ToResponse(appointment));
    }

    /// <summary>
    /// Runs one change as a bounded series of attempts, each in its own transaction — the same
    /// shape as <see cref="AppointmentBookingService.BookAsync"/>. A lost race on the slot row (or
    /// a deadlock) rolls the attempt back and re-runs it from the first read.
    /// </summary>
    private async Task<AppointmentChangeResult> RunAsync(
        Func<CancellationToken, Task<AppointmentChangeResult>> attempt,
        CancellationToken cancellationToken)
    {
        for (var attemptNumber = 1; ; attemptNumber++)
        {
            try
            {
                var result = await _unitOfWork.ExecuteInTransactionAsync(attempt, cancellationToken);

                // A retry that now finds the new slot Booked lost the race it was retrying, and
                // says so in the race's words, as booking does.
                return attemptNumber > 1
                    && result is AppointmentNewSlotNotAvailableResult { CurrentStatus: SlotStatus.Booked }
                    ? new AppointmentSlotTakenResult()
                    : result;
            }
            catch (SlotAlreadyBookedException)
            {
                // ux_appointments_slot: another appointment holds the new slot. The answer, not a
                // reason to retry; the rollback left this appointment where it was.
                return new AppointmentSlotTakenResult();
            }
            catch (ConcurrentUpdateException) when (attemptNumber < ConcurrencyRetry.MaxAttempts)
            {
                // Rolled back and the tracker cleared; go round and read everything again.
            }
            catch (ConcurrentUpdateException)
            {
                return new AppointmentContendedResult();
            }
        }
    }

    /// <summary>
    /// Locks and returns the appointment, or null when it does not exist or — for a patient caller —
    /// is not theirs. The two are one answer so a booking token learns nothing about ids it does
    /// not own.
    /// </summary>
    private async Task<AppointmentEntity?> LockAppointmentAsync(
        Guid appointmentId,
        AppointmentCaller caller,
        CancellationToken cancellationToken)
    {
        var appointment = await _appointmentRepository.GetTrackedForUpdateAsync(
            appointmentId, cancellationToken);

        return appointment is not null
            && (caller.PatientId is null || caller.PatientId == appointment.PatientId)
            ? appointment
            : null;
    }

    /// <summary>
    /// True from <c>WindowHours</c> before the start onwards. The comparison is inclusive, so the
    /// exact boundary is already inside, and with a zero window an appointment that has started
    /// can no longer be changed.
    /// </summary>
    private bool IsInsideWindow(AppointmentEntity appointment, DateTime nowUtc) =>
        nowUtc >= appointment.StartUtc.AddHours(-WindowHours);

    /// <summary>
    /// Gives back the slot an appointment is leaving.
    /// </summary>
    /// <remarks>
    /// A booked slot becomes available again. A flagged slot is removed instead: a schedule change
    /// already put it outside the doctor's hours, and making it available could collide with a
    /// fresh slot at the same instant on <c>ux_slots_doctor_start</c>, which excludes only flagged
    /// rows. A missing slot (schedule revision hard-deleted it) needs nothing. Any other status
    /// would mean the slot and the appointment disagree, and the slot is left as it is rather than
    /// guessed at.
    /// </remarks>
    private void ReleaseSlot(Slot? slot, string actor)
    {
        switch (slot?.Status)
        {
            case SlotStatus.Booked:
                slot.Status = SlotStatus.Available;
                slot.UpdatedBy = actor;
                break;
            case SlotStatus.Flagged:
                _slotRepository.RemoveRange([slot]);
                break;
        }
    }
}
