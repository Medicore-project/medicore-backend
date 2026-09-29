using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Scheduling;

/// <summary>
/// Detects whether a doctor schedule collides with one the doctor already has (SCRUM-32 AC4).
/// </summary>
/// <remarks>
/// This is an application-level check rather than a database constraint on purpose. Overlap is a
/// range comparison — 09:00-12:00 and 11:00-14:00 collide without sharing any column value — so no
/// unique index can express it. PostgreSQL could with an <c>EXCLUDE</c> constraint, but that needs
/// the <c>btree_gist</c> extension plus a grant for <c>appointment_svc</c>, which is not worth it
/// for a check this cheap to run in memory.
/// </remarks>
public interface IScheduleOverlapDetector
{
    /// <summary>
    /// Returns the first schedule in <paramref name="existing"/> that collides with
    /// <paramref name="candidate"/>, or null when the candidate is free of conflicts.
    /// </summary>
    /// <remarks>
    /// Two schedules collide when they belong to the same doctor and weekday and their working
    /// windows and effective date ranges both intersect. The candidate is matched by
    /// <see cref="DoctorSchedule.ScheduleId"/> and excluded from its own check, so this is safe to
    /// call when updating an existing schedule as well as when creating one.
    /// </remarks>
    DoctorSchedule? FindConflict(
        DoctorSchedule candidate,
        IReadOnlyCollection<DoctorSchedule> existing);
}
