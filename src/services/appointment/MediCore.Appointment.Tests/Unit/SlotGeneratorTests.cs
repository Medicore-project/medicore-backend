using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Scheduling;
using Microsoft.Extensions.Options;

namespace MediCore.Appointment.Tests.Unit;

public sealed class SlotGeneratorTests
{
    private static readonly Guid DoctorId = Guid.Parse("8e3da03b-2c72-4f02-b09d-4f7bc2c1b981");

    [Fact]
    public void Current_horizon_uses_Colombo_today_and_the_configured_day_count()
    {
        var generator = CreateGenerator(new DateTimeOffset(2026, 9, 20, 20, 0, 0, TimeSpan.Zero));

        var horizon = generator.CurrentHorizon();

        // 20:00 UTC is 01:30 on the following day in Colombo.
        Assert.Equal(new DateOnly(2026, 9, 21), horizon.From);
        Assert.Equal(new DateOnly(2026, 11, 20), horizon.To);
    }

    [Fact]
    public void Generate_creates_all_recurring_slots_through_the_60_day_horizon()
    {
        var generator = CreateGenerator();
        var schedule = Schedule(DayOfWeek.Sunday, new TimeOnly(9, 0), new TimeOnly(10, 0));
        var horizon = generator.CurrentHorizon();

        var slots = generator.Generate(schedule, horizon.From, horizon.To, [], []);

        // 20 Sep through 19 Nov 2026 contains nine Sundays, each with two 30-minute slots.
        Assert.Equal(18, slots.Count);
        Assert.Equal(new DateOnly(2026, 9, 20), slots[0].SlotDate);
        Assert.Equal(new DateOnly(2026, 11, 15), slots[^1].SlotDate);
    }

    [Fact]
    public void Generate_expands_a_weekly_schedule_into_UTC_slots()
    {
        var generator = CreateGenerator();
        var schedule = Schedule(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var slots = generator.Generate(
            schedule,
            new DateOnly(2026, 9, 21),
            new DateOnly(2026, 9, 27),
            [],
            []);

        Assert.Equal(2, slots.Count);
        Assert.All(slots, slot =>
        {
            Assert.Equal(DoctorId, slot.DoctorId);
            Assert.Equal(schedule.Id, slot.DoctorScheduleId);
            Assert.Equal(new DateOnly(2026, 9, 21), slot.SlotDate);
            Assert.Equal(30, slot.DurationMinutes);
            Assert.Equal(SlotStatus.Available, slot.Status);
        });
        Assert.Equal(new DateTime(2026, 9, 21, 3, 30, 0, DateTimeKind.Utc), slots[0].StartUtc);
        Assert.Equal(new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc), slots[0].EndUtc);
        Assert.Equal(new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc), slots[1].StartUtc);
    }

    [Fact]
    public void Generate_skips_holidays_and_approved_leave_but_not_pending_leave()
    {
        var generator = CreateGenerator();
        var schedule = Schedule(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0));
        var holiday = new PublicHoliday { Date = new DateOnly(2026, 9, 21), Name = "Clinic closure" };
        var pendingLeave = Leave(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 28), LeaveStatus.Pending);
        var approvedLeave = Leave(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), LeaveStatus.Approved);

        var slots = generator.Generate(
            schedule,
            new DateOnly(2026, 9, 21),
            new DateOnly(2026, 10, 5),
            [holiday],
            [pendingLeave, approvedLeave]);

        Assert.Equal(2, slots.Count);
        Assert.All(slots, slot => Assert.Equal(new DateOnly(2026, 9, 28), slot.SlotDate));
    }

    [Fact]
    public void Generate_respects_effective_dates_and_discards_an_incomplete_final_slot()
    {
        var generator = CreateGenerator();
        var schedule = Schedule(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 20));
        schedule.EffectiveFrom = new DateOnly(2026, 9, 28);
        schedule.EffectiveTo = new DateOnly(2026, 9, 28);

        var slots = generator.Generate(
            schedule,
            new DateOnly(2026, 9, 21),
            new DateOnly(2026, 10, 5),
            [],
            []);

        Assert.Equal(2, slots.Count);
        Assert.All(slots, slot => Assert.Equal(new DateOnly(2026, 9, 28), slot.SlotDate));
        Assert.Equal(new TimeSpan(3, 30, 0), slots[0].StartUtc.TimeOfDay);
        Assert.Equal(new TimeSpan(4, 0, 0), slots[1].StartUtc.TimeOfDay);
    }

    [Fact]
    public void Generate_returns_no_slots_for_inactive_or_deleted_schedules()
    {
        var generator = CreateGenerator();
        var schedule = Schedule(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0));
        schedule.IsActive = false;

        var inactiveSlots = generator.Generate(
            schedule, new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 21), [], []);

        schedule.IsActive = true;
        schedule.IsDeleted = true;
        var deletedSlots = generator.Generate(
            schedule, new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 21), [], []);

        Assert.Empty(inactiveSlots);
        Assert.Empty(deletedSlots);
    }

    private static SlotGenerator CreateGenerator(DateTimeOffset? now = null) => new(
        new FixedTimeProvider(now ?? new DateTimeOffset(2026, 9, 20, 6, 0, 0, TimeSpan.Zero)),
        Options.Create(new SchedulingOptions { SlotHorizonDays = 60 }));

    private static DoctorSchedule Schedule(DayOfWeek day, TimeOnly start, TimeOnly end) => new()
    {
        Id = Guid.Parse("a417e0af-97a3-4c83-8bd4-b3fc06323c80"),
        DoctorId = DoctorId,
        DayOfWeek = day,
        StartTime = start,
        EndTime = end,
        SlotDurationMinutes = 30,
        EffectiveFrom = new DateOnly(2026, 1, 1),
    };

    private static DoctorLeave Leave(DateOnly from, DateOnly to, string status) => new()
    {
        DoctorId = DoctorId,
        StartDate = from,
        EndDate = to,
        Status = status,
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
