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
}
