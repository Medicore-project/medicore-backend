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
    private readonly IOutboxMessageRepository _outboxRepository;
    private readonly IAppointmentHistoryRepository _historyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationPolicyOptions _cancellationPolicy;

    public AppointmentLifecycleService(
        IAppointmentRepository appointmentRepository,
        ISlotRepository slotRepository,
        IOutboxMessageRepository outboxRepository,
        IAppointmentHistoryRepository historyRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IOptions<CancellationPolicyOptions> cancellationPolicy)
    {
        _appointmentRepository = appointmentRepository;
        _slotRepository = slotRepository;
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

        await ReleaseSlotAsync(appointment.SlotId, caller.Actor, cancellationToken);

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
                return await _unitOfWork.ExecuteInTransactionAsync(attempt, cancellationToken);
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
    private async Task ReleaseSlotAsync(Guid slotId, string actor, CancellationToken cancellationToken)
    {
        var slot = await _slotRepository.GetTrackedBySlotIdAsync(slotId, cancellationToken);

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
