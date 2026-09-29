namespace MediCore.Appointment.Application.Exceptions;

/// <summary>
/// Thrown when a schedule would collide with one the doctor already has (SCRUM-32 AC4).
/// Controllers map it to 409.
/// </summary>
public sealed class ScheduleOverlapException : Exception
{
    public ScheduleOverlapException(Guid conflictingScheduleId, DayOfWeek dayOfWeek)
        : base(
            $"This schedule overlaps an existing {dayOfWeek} schedule for the same doctor "
            + $"(schedule {conflictingScheduleId}).")
    {
        ConflictingScheduleId = conflictingScheduleId;
        DayOfWeek = dayOfWeek;
    }

    /// <summary>Business key of the schedule that was already there.</summary>
    public Guid ConflictingScheduleId { get; }

    /// <summary>The weekday both schedules fall on.</summary>
    public DayOfWeek DayOfWeek { get; }
}
