using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Scheduling;

namespace MediCore.Appointment.Tests.Unit;

public sealed class SlotReconcilerTests
{
    private readonly SlotReconciler _reconciler = new();
    private static readonly Guid DoctorId = Guid.Parse("bdf497f6-4186-4175-a026-8dc8ef125531");
    private static readonly DateTime Now = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Reconcile_flags_booked_slots_outside_the_new_schedule_and_removes_free_ones()
    {
        var wanted = Slot(9, SlotStatus.Available);
        var availableOutsideSchedule = Slot(10, SlotStatus.Available);
        var bookedOutsideSchedule = Slot(11, SlotStatus.Booked);

        var plan = _reconciler.Reconcile(
            [wanted],
            [wanted, availableOutsideSchedule, bookedOutsideSchedule],
            Now);

        Assert.Empty(plan.ToInsert);
        Assert.Equal([availableOutsideSchedule], plan.ToDelete);
        Assert.Equal([bookedOutsideSchedule], plan.ToFlag);
    }

    [Fact]
    public void Reconcile_is_idempotent_when_the_desired_slots_already_exist()
    {
        var existing = Slot(9, SlotStatus.Available);

        var plan = _reconciler.Reconcile([existing], [existing], Now);

        Assert.False(plan.HasChanges);
        Assert.Empty(plan.ToInsert);
        Assert.Empty(plan.ToDelete);
        Assert.Empty(plan.ToFlag);
    }

    [Fact]
    public void Reconcile_does_not_change_past_slots()
    {
        var pastAvailable = Slot(9, SlotStatus.Available, new DateTime(2026, 9, 19, 9, 0, 0, DateTimeKind.Utc));
        var pastDesired = Slot(10, SlotStatus.Available, new DateTime(2026, 9, 19, 10, 0, 0, DateTimeKind.Utc));

        var plan = _reconciler.Reconcile([pastDesired], [pastAvailable], Now);

        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void Reconcile_allows_a_fresh_slot_alongside_a_flagged_historical_slot()
    {
        var flagged = Slot(9, SlotStatus.Flagged);
        var desired = Slot(9, SlotStatus.Available);

        var plan = _reconciler.Reconcile([desired], [flagged], Now);

        Assert.Equal([desired], plan.ToInsert);
        Assert.Empty(plan.ToDelete);
        Assert.Empty(plan.ToFlag);
    }

    private static Slot Slot(int hour, string status, DateTime? start = null)
    {
        var startUtc = start ?? new DateTime(2026, 9, 21, hour, 0, 0, DateTimeKind.Utc);
        return new Slot
        {
            DoctorId = DoctorId,
            StartUtc = startUtc,
            EndUtc = startUtc.AddMinutes(30),
            Status = status,
        };
    }
}
