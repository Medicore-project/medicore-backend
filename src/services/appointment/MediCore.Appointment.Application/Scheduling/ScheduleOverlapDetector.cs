using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Scheduling;

/// <inheritdoc cref="IScheduleOverlapDetector"/>
public sealed class ScheduleOverlapDetector : IScheduleOverlapDetector
{
    /// <inheritdoc />
    public DoctorSchedule? FindConflict(
        DoctorSchedule candidate,
        IReadOnlyCollection<DoctorSchedule> existing)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existing);

        foreach (var other in existing)
        {
            if (Collides(candidate, other))
            {
                return other;
            }
        }

        return null;
    }

    private static bool Collides(DoctorSchedule candidate, DoctorSchedule other)
    {
        // A schedule never conflicts with itself, so an update can be validated the same way as a
        // create.
        if (other.ScheduleId == candidate.ScheduleId)
        {
            return false;
        }

        if (other.IsDeleted || other.DoctorId != candidate.DoctorId || other.DayOfWeek != candidate.DayOfWeek)
        {
            return false;
        }

        // Paused schedules still count. They generate no slots today, but they can be reactivated,
        // and a schedule created into the gap while one was paused would collide the moment it came
        // back. Checking them keeps that trap from ever being set.
        return TimesOverlap(candidate, other) && EffectiveRangesOverlap(candidate, other);
    }

    /// <summary>
    /// Half-open comparison, so windows that merely touch do not collide: 09:00-12:00 and
    /// 13:00-17:00 are a split shift, and even 09:00-12:00 and 12:00-15:00 are back-to-back rather
    /// than overlapping.
    /// </summary>
    private static bool TimesOverlap(DoctorSchedule left, DoctorSchedule right) =>
        left.StartTime < right.EndTime && right.StartTime < left.EndTime;

    /// <summary>
    /// A null <see cref="DoctorSchedule.EffectiveTo"/> means open-ended, so it is treated as the
    /// maximum date rather than excluded from the comparison.
    /// </summary>
    private static bool EffectiveRangesOverlap(DoctorSchedule left, DoctorSchedule right) =>
        left.EffectiveFrom <= (right.EffectiveTo ?? DateOnly.MaxValue)
        && right.EffectiveFrom <= (left.EffectiveTo ?? DateOnly.MaxValue);
}
