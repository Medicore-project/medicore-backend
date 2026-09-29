using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>Data-access contract for <see cref="DoctorLeave"/>.</summary>
public interface IDoctorLeaveRepository
{
    /// <summary>
    /// Approved leave for one doctor overlapping <paramref name="from"/>..<paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// Filtered to <see cref="LeaveStatus.Approved"/> in the query rather than in memory: pending
    /// and rejected requests can never affect slot generation, so there is no reason to load them.
    /// <see cref="Scheduling.ISlotGenerator"/> re-checks the status anyway, so passing an
    /// unfiltered set stays correct — just wasteful.
    /// </remarks>
    Task<IReadOnlyList<DoctorLeave>> GetApprovedForDoctorBetweenAsync(
        Guid doctorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>Every leave request for one doctor whatever its status, most recent first.</summary>
    Task<IReadOnlyList<DoctorLeave>> GetAllForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The approval queue: every request still awaiting a decision, across all doctors, with the
    /// soonest start date first.
    /// </summary>
    Task<IReadOnlyList<DoctorLeave>> GetPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a change-tracked request by its business key, for review or withdrawal.</summary>
    Task<DoctorLeave?> GetTrackedByLeaveIdAsync(
        Guid leaveId,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new leave request for insertion (not yet committed).</summary>
    Task AddAsync(DoctorLeave leave, CancellationToken cancellationToken = default);
}
