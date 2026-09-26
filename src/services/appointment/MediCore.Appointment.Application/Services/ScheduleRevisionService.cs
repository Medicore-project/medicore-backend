using MediCore.Appointment.Application.Concurrency;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using Microsoft.Extensions.Logging;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IScheduleRevisionService"/>
public sealed class ScheduleRevisionService : IScheduleRevisionService
{
    private readonly IDoctorScheduleRepository _scheduleRepository;
    private readonly ISlotRepository _slotRepository;
    private readonly IPublicHolidayRepository _holidayRepository;
    private readonly IDoctorLeaveRepository _leaveRepository;
    private readonly ISlotGenerator _slotGenerator;
    private readonly ISlotReconciler _reconciler;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScheduleRevisionService> _logger;

    public ScheduleRevisionService(
        IDoctorScheduleRepository scheduleRepository,
        ISlotRepository slotRepository,
        IPublicHolidayRepository holidayRepository,
        IDoctorLeaveRepository leaveRepository,
        ISlotGenerator slotGenerator,
        ISlotReconciler reconciler,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ScheduleRevisionService> logger)
    {
        _scheduleRepository = scheduleRepository;
        _slotRepository = slotRepository;
        _holidayRepository = holidayRepository;
        _leaveRepository = leaveRepository;
        _slotGenerator = slotGenerator;
        _reconciler = reconciler;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// SCRUM-35: re-run whole if a slot changed under it — most often a booking between the read
    /// and the save. Reconciliation is idempotent (SCRUM-32 AC5), so the re-run simply re-reads,
    /// sees the slot as Booked and flags it instead of deleting it. Every caller has committed its
    /// own change before calling, so the cleared change tracker loses nothing of theirs.
    /// </remarks>
    public Task<SlotReconciliationSummary> ReconcileDoctorAsync(
        Guid doctorId,
        string flagReason,
        CancellationToken cancellationToken = default) =>
        ConcurrencyRetry.RunAsync(
            token => ReconcileDoctorOnceAsync(doctorId, flagReason, token),
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>Retried as a whole for the same reason as <see cref="ReconcileDoctorAsync"/>.</remarks>
    public Task<SlotReconciliationSummary> ReconcileAllDoctorsAsync(
        string flagReason,
        CancellationToken cancellationToken = default) =>
        ConcurrencyRetry.RunAsync(
            token => ReconcileAllDoctorsOnceAsync(flagReason, token),
            cancellationToken);

    private async Task<SlotReconciliationSummary> ReconcileDoctorOnceAsync(
        Guid doctorId,
        string flagReason,
        CancellationToken cancellationToken)
    {
        var (from, to) = _slotGenerator.CurrentHorizon();

        // Holidays are clinic-wide, so they are fetched once per doctor rather than shared. That is
        // a small amount of repeated work in the clinic-wide path, accepted to keep each doctor's
        // reconciliation a self-contained unit.
        var holidays = await _holidayRepository.GetBetweenAsync(from, to, cancellationToken);

        var summary = await ReconcileDoctorCoreAsync(
            doctorId, from, to, holidays, flagReason, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return summary;
    }

    private async Task<SlotReconciliationSummary> ReconcileAllDoctorsOnceAsync(
        string flagReason,
        CancellationToken cancellationToken)
    {
        var (from, to) = _slotGenerator.CurrentHorizon();
        var holidays = await _holidayRepository.GetBetweenAsync(from, to, cancellationToken);
        var doctorIds = await _scheduleRepository.GetDoctorIdsWithActiveSchedulesAsync(cancellationToken);

        var summary = SlotReconciliationSummary.Empty;

        foreach (var doctorId in doctorIds)
        {
            summary = summary.Combine(await ReconcileDoctorCoreAsync(
                doctorId, from, to, holidays, flagReason, cancellationToken));
        }

        // One transaction for the whole fan-out: a holiday either applies to every doctor or to
        // none, never to the first half of the list.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return summary;
    }

    /// <summary>
    /// Stages one doctor's changes without committing, so the caller decides the transaction
    /// boundary.
    /// </summary>
    private async Task<SlotReconciliationSummary> ReconcileDoctorCoreAsync(
        Guid doctorId,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<PublicHoliday> holidays,
        string flagReason,
        CancellationToken cancellationToken)
    {
        var schedules = await _scheduleRepository.GetActiveForDoctorAsync(
            doctorId, from, to, cancellationToken);

        var leaves = await _leaveRepository.GetApprovedForDoctorBetweenAsync(
            doctorId, from, to, cancellationToken);

        // The desired calendar is the union of every active schedule, not one schedule in
        // isolation. Reconciling against a single schedule would delete the slots belonging to the
        // doctor's other weekdays, since those are equally "not covered" by the one being edited.
        var desired = new List<Slot>();

        foreach (var schedule in schedules)
        {
            desired.AddRange(_slotGenerator.Generate(schedule, from, to, holidays, leaves));
        }

        var existing = await _slotRepository.GetTrackedForDoctorBetweenAsync(
            doctorId, from, to, cancellationToken);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var plan = _reconciler.Reconcile(desired, existing, nowUtc);

        if (!plan.HasChanges)
        {
            return new SlotReconciliationSummary(DoctorsProcessed: 1, 0, 0, 0);
        }

        if (plan.ToInsert.Count > 0)
        {
            await _slotRepository.AddRangeAsync(plan.ToInsert, cancellationToken);
        }

        if (plan.ToDelete.Count > 0)
        {
            _slotRepository.RemoveRange(plan.ToDelete);
        }

        foreach (var slot in plan.ToFlag)
        {
            slot.Status = SlotStatus.Flagged;
            slot.FlaggedReason = flagReason;
            slot.FlaggedAtUtc = nowUtc;
        }

        if (plan.ToFlag.Count > 0)
        {
            // Worth a warning rather than information: every flagged slot is a patient whose
            // appointment no longer fits and who someone has to contact.
            _logger.LogWarning(
                "Reconciling doctor {DoctorId} flagged {FlaggedCount} booked slot(s): {Reason}",
                doctorId,
                plan.ToFlag.Count,
                flagReason);
        }

        _logger.LogInformation(
            "Reconciled doctor {DoctorId} over {From}..{To}: +{Created} -{Removed} !{Flagged}",
            doctorId,
            from,
            to,
            plan.ToInsert.Count,
            plan.ToDelete.Count,
            plan.ToFlag.Count);

        return new SlotReconciliationSummary(
            DoctorsProcessed: 1,
            SlotsCreated: plan.ToInsert.Count,
            SlotsRemoved: plan.ToDelete.Count,
            SlotsFlagged: plan.ToFlag.Count);
    }
}
