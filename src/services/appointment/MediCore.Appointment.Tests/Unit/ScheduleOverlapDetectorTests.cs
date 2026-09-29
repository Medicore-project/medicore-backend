using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Scheduling;

namespace MediCore.Appointment.Tests.Unit;

public sealed class ScheduleOverlapDetectorTests
{
    private readonly ScheduleOverlapDetector _detector = new();
    private static readonly Guid DoctorId = Guid.Parse("6642f0f0-23c0-442c-a8e8-0488fdfcbbd6");

    [Fact]
    public void FindConflict_returns_the_colliding_schedule_for_the_same_doctor_day_and_dates()
    {
        var existing = Schedule(new TimeOnly(9, 0), new TimeOnly(12, 0));
        var candidate = Schedule(new TimeOnly(11, 30), new TimeOnly(14, 0));

        var conflict = _detector.FindConflict(candidate, [existing]);

        Assert.Same(existing, conflict);
    }

    [Fact]
    public void FindConflict_allows_back_to_back_windows_and_non_overlapping_effective_ranges()
    {
        var morning = Schedule(new TimeOnly(9, 0), new TimeOnly(12, 0));
        var afternoon = Schedule(new TimeOnly(12, 0), new TimeOnly(17, 0));
        var later = Schedule(new TimeOnly(10, 0), new TimeOnly(11, 0));
        morning.EffectiveTo = new DateOnly(2026, 12, 31);
        later.EffectiveFrom = new DateOnly(2027, 1, 1);

        Assert.Null(_detector.FindConflict(afternoon, [morning]));
        Assert.Null(_detector.FindConflict(later, [morning]));
    }

    [Fact]
    public void FindConflict_ignores_the_schedule_being_updated_and_another_doctor()
    {
        var candidate = Schedule(new TimeOnly(9, 0), new TimeOnly(12, 0));
        var sameSchedule = Schedule(new TimeOnly(9, 0), new TimeOnly(12, 0));
        sameSchedule.ScheduleId = candidate.ScheduleId;
        var otherDoctor = Schedule(new TimeOnly(9, 0), new TimeOnly(12, 0));
        otherDoctor.DoctorId = Guid.NewGuid();

        Assert.Null(_detector.FindConflict(candidate, [sameSchedule, otherDoctor]));
    }

    private static DoctorSchedule Schedule(TimeOnly start, TimeOnly end) => new()
    {
        DoctorId = DoctorId,
        DayOfWeek = DayOfWeek.Monday,
        StartTime = start,
        EndTime = end,
        SlotDurationMinutes = 30,
        EffectiveFrom = new DateOnly(2026, 1, 1),
    };
}
