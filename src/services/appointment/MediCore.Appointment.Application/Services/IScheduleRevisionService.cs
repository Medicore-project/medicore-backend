namespace MediCore.Appointment.Application.Services;

/// <summary>What a reconciliation actually changed.</summary>
/// <param name="DoctorsProcessed">How many doctors were reconciled.</param>
/// <param name="SlotsCreated">Slots inserted because a schedule now covers their time.</param>
/// <param name="SlotsRemoved">Free slots deleted because no schedule covers their time any more.</param>
/// <param name="SlotsFlagged">
/// Booked slots kept but marked for attention — each one is a patient who needs rescheduling.
/// </param>
public sealed record SlotReconciliationSummary(
    int DoctorsProcessed,
    int SlotsCreated,
    int SlotsRemoved,
    int SlotsFlagged)
{
    /// <summary>A reconciliation that found nothing to do.</summary>
    public static SlotReconciliationSummary Empty { get; } = new(0, 0, 0, 0);

    /// <summary>Whether anything was written.</summary>
    public bool HasChanges => SlotsCreated > 0 || SlotsRemoved > 0 || SlotsFlagged > 0;

    internal SlotReconciliationSummary Combine(SlotReconciliationSummary other) => new(
        DoctorsProcessed + other.DoctorsProcessed,
        SlotsCreated + other.SlotsCreated,
        SlotsRemoved + other.SlotsRemoved,
        SlotsFlagged + other.SlotsFlagged);
}

/// <summary>
/// Brings stored slots back in line with what the schedules, holidays and approved leave currently
/// imply (SCRUM-32 AC2 and AC3).
/// </summary>
/// <remarks>
/// Deliberately expressed as "make the calendar correct for this doctor" rather than as a set of
/// per-trigger operations. Creating, editing, pausing or deleting a schedule, declaring or
/// withdrawing a holiday, and approving or revoking leave all change the same thing — which slots
/// ought to exist — so they all reconcile the same way and there is only one piece of logic to get
/// right.
/// </remarks>
public interface IScheduleRevisionService
{
    /// <summary>
    /// Reconciles one doctor's slots across the configured horizon.
    /// </summary>
    /// <param name="doctorId">The doctor whose calendar should be rebuilt.</param>
    /// <param name="flagReason">
    /// Recorded on any booked slot that no longer fits, so a receptionist can see why it needs
    /// attention. Should name the cause, for example "Doctor leave approved for 5-9 October".
    /// </param>
    Task<SlotReconciliationSummary> ReconcileDoctorAsync(
        Guid doctorId,
        string flagReason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles every doctor holding an active schedule — the clinic-wide case, such as a public
    /// holiday being declared or withdrawn.
    /// </summary>
    Task<SlotReconciliationSummary> ReconcileAllDoctorsAsync(
        string flagReason,
        CancellationToken cancellationToken = default);
}
