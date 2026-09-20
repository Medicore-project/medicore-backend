using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>Data-access contract for <see cref="DoctorSchedule"/>.</summary>
public interface IDoctorScheduleRepository
{
    /// <summary>
    /// Active schedules for one doctor whose effective range touches
    /// <paramref name="from"/>..<paramref name="to"/>. These are the schedules slot generation
    /// expands.
    /// </summary>
    Task<IReadOnlyList<DoctorSchedule>> GetActiveForDoctorAsync(
        Guid doctorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every schedule for one doctor on one weekday, used for the AC4 overlap check.
    /// </summary>
    /// <remarks>
    /// Includes paused schedules, because <see cref="Scheduling.IScheduleOverlapDetector"/> treats
    /// them as conflicting — they can be reactivated later.
    /// </remarks>
    Task<IReadOnlyList<DoctorSchedule>> GetForOverlapCheckAsync(
        Guid doctorId,
        DayOfWeek dayOfWeek,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Distinct doctors holding at least one active schedule. Used to fan a clinic-wide change,
    /// such as a new public holiday, out to everyone it affects.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetDoctorIdsWithActiveSchedulesAsync(
        CancellationToken cancellationToken = default);
}
