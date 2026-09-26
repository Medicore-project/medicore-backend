using System.Text.Json;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using MediCore.Contracts.Events.Appointment;
using Microsoft.Extensions.Options;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Tests.Unit;

public sealed class AppointmentLifecycleServiceTests
{
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherPatientId = Guid.Parse("2222222a-2222-2222-2222-222222222222");
    private static readonly Guid SlotId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AppointmentId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid NewSlotId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid OtherDoctorId = Guid.Parse("1111111a-1111-1111-1111-111111111111");

    private static readonly DateTime Now = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>Three days out: comfortably outside the default 24-hour window.</summary>
    private static readonly DateTime Start = Now.AddDays(3);

    private static readonly AppointmentCaller Desk = new("desk@medicore.test");

    // ── Cancel: the happy path ───────────────────────────────────────────────

    [Fact]
    public async Task Cancelling_outside_the_window_cancels_releases_the_slot_and_records_it()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CancelAsync(AppointmentId, "  Travelling  ", Desk, "corr-1");

        var changed = Assert.IsType<AppointmentChangedResult>(result);
        Assert.Equal(AppointmentStatus.Cancelled, changed.Appointment.Status);
        Assert.Equal(AppointmentStatus.Cancelled, fixture.Appointment.Status);
        Assert.Equal("desk@medicore.test", fixture.Appointment.UpdatedBy);

        Assert.Equal(SlotStatus.Available, fixture.Slot.Status);
        Assert.Equal("desk@medicore.test", fixture.Slot.UpdatedBy);

        var entry = Assert.Single(fixture.History.Added);
        Assert.Equal(AppointmentId, entry.AppointmentId);
        Assert.Equal(AppointmentHistoryAction.Cancelled, entry.Action);
        Assert.Equal(AppointmentStatus.Booked, entry.FromStatus);
        Assert.Equal(AppointmentStatus.Cancelled, entry.ToStatus);
        Assert.Equal(SlotId, entry.FromSlotId);
        Assert.Null(entry.ToSlotId);
        Assert.Equal(Start, entry.FromStartUtc);
        Assert.Equal("Travelling", entry.Reason);
        Assert.Equal("desk@medicore.test", entry.Actor);
        Assert.Equal(Now, entry.OccurredAtUtc);

        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_cancellation_is_announced_on_the_same_partition_as_its_booking()
    {
        // The ticket's ordering note: keyed by the appointment, as appointment.booked is, so
        // billing can never see the cancellation first.
        var fixture = new Fixture();

        await fixture.Service.CancelAsync(AppointmentId, "Travelling", Desk, "corr-1");

        var message = Assert.Single(fixture.Outbox.Added);
        Assert.Equal("appointment.cancelled", message.EventType);
        Assert.Equal("appointment-events", message.Topic);
        Assert.Equal(AppointmentId.ToString(), message.EventKey);
        Assert.Equal("corr-1", message.CorrelationId);

        var published = JsonSerializer.Deserialize<AppointmentCancelledEvent>(
            message.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(AppointmentId, published.AppointmentId);
        Assert.Equal("Travelling", published.Reason);
    }

    [Fact]
    public async Task The_appointment_is_locked_before_anything_else_is_read()
    {
        // The row lock is what serializes two changes to one appointment, so it must come first
        // and be held until the commit.
        var fixture = new Fixture();

        await fixture.Service.CancelAsync(AppointmentId, "Travelling", Desk, "corr-1");

        Assert.Equal(
            ["begin", $"lock appointment {AppointmentId}", $"read slot {SlotId}", "save", "commit"],
            fixture.Log);
    }

    // ── Cancel: the cancellation window ──────────────────────────────────────

    [Fact]
    public async Task Cancelling_inside_the_window_is_refused_with_the_policy_and_nothing_changes()
    {
        var fixture = new Fixture();
        fixture.Appointment.StartUtc = Now.AddHours(5);

        var result = await fixture.Service.CancelAsync(AppointmentId, "Travelling", Desk, "corr-1");

        var refused = Assert.IsType<AppointmentInsideCancellationWindowResult>(result);
        Assert.Equal(24, refused.WindowHours);
        Assert.Equal(Now.AddHours(5), refused.StartUtc);
        AssertNothingChanged(fixture);
    }

    [Theory]
    [InlineData(0, false)] // exactly 24 hours before: already inside
    [InlineData(-1, false)] // one second inside
    [InlineData(1, true)] // one second outside
    public async Task The_window_boundary_is_inclusive(int secondsPastBoundary, bool allowed)
    {
        var fixture = new Fixture();
        fixture.Appointment.StartUtc = Now.AddHours(24).AddSeconds(secondsPastBoundary);

        var result = await fixture.Service.CancelAsync(AppointmentId, "Travelling", Desk, "corr-1");

        Assert.Equal(allowed, result is AppointmentChangedResult);
    }

    [Fact]
    public async Task The_window_is_configurable()
    {
        var fixture = new Fixture(windowHours: 2);
        fixture.Appointment.StartUtc = Now.AddHours(3);

        var result = await fixture.Service.CancelAsync(AppointmentId, "Travelling", Desk, "corr-1");

        Assert.IsType<AppointmentChangedResult>(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)] // a nonsensical setting is read as no window, never as "any time, even after"
    public async Task With_no_window_an_appointment_can_be_cancelled_until_it_starts(int windowHours)
    {
        var upcoming = new Fixture(windowHours);
        upcoming.Appointment.StartUtc = Now.AddMinutes(1);
        var started = new Fixture(windowHours);
        started.Appointment.StartUtc = Now;

        var upcomingResult = await upcoming.Service.CancelAsync(AppointmentId, "Ill", Desk, "corr-1");
        var startedResult = await started.Service.CancelAsync(AppointmentId, "Ill", Desk, "corr-1");

        Assert.IsType<AppointmentChangedResult>(upcomingResult);
        var refused = Assert.IsType<AppointmentInsideCancellationWindowResult>(startedResult);
        Assert.Equal(0, refused.WindowHours);
        AssertNothingChanged(started);
    }

    [Fact]
    public async Task The_window_binds_staff_as_well_as_patients()
    {
        var fixture = new Fixture();
        fixture.Appointment.StartUtc = Now.AddHours(1);
        var admin = new AppointmentCaller("admin@medicore.test", StaffId: Guid.NewGuid());

        var result = await fixture.Service.CancelAsync(AppointmentId, "Travelling", admin, "corr-1");

        Assert.IsType<AppointmentInsideCancellationWindowResult>(result);
    }

    // ── Cancel: invalid transitions ──────────────────────────────────────────

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task Only_a_booked_appointment_can_be_cancelled(string status)
    {
        var fixture = new Fixture();
        fixture.Appointment.Status = status;

        var result = await fixture.Service.CancelAsync(AppointmentId, "Travelling", Desk, "corr-1");

        var refused = Assert.IsType<AppointmentInvalidTransitionResult>(result);
        Assert.Equal(status, refused.CurrentStatus);
        Assert.Equal(AppointmentHistoryAction.Cancelled, refused.Action);
        AssertNothingChanged(fixture, expectedStatus: status);
    }

    [Fact]
    public async Task The_status_is_checked_before_the_window()
    {
        // A visit completed an hour ago is refused as completed: that is the true objection, and
        // "too late to cancel" would suggest it could have been cancelled earlier.
        var fixture = new Fixture();
        fixture.Appointment.Status = AppointmentStatus.Completed;
        fixture.Appointment.StartUtc = Now.AddHours(-1);

        var result = await fixture.Service.CancelAsync(AppointmentId, "Travelling", Desk, "corr-1");

        Assert.IsType<AppointmentInvalidTransitionResult>(result);
    }

    [Fact]
    public async Task An_unknown_appointment_is_not_found()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CancelAsync(Guid.NewGuid(), "Travelling", Desk, "corr-1");

        Assert.IsType<AppointmentNotFoundResult>(result);
        AssertNothingChanged(fixture);
    }

    // ── Cancel: who may ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_patient_can_cancel_their_own_appointment()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CancelAsync(
            AppointmentId, "Travelling", new AppointmentCaller("patient", PatientId), "corr-1");

        Assert.IsType<AppointmentChangedResult>(result);
    }

    [Fact]
    public async Task A_patient_cannot_cancel_someone_elses_and_cannot_tell_it_exists()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CancelAsync(
            AppointmentId, "Travelling", new AppointmentCaller("patient", OtherPatientId), "corr-1");

        Assert.IsType<AppointmentNotFoundResult>(result);
        AssertNothingChanged(fixture);
    }

    // ── Cancel: the slot ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_flagged_slot_is_removed_rather_than_released()
    {
        // A schedule change put it outside the doctor's hours; making it Available would offer a
        // time the doctor no longer works, and could collide with a fresh slot at that instant.
        var fixture = new Fixture();
        fixture.Slot.Status = SlotStatus.Flagged;

        var result = await fixture.Service.CancelAsync(AppointmentId, "Travelling", Desk, "corr-1");

        Assert.IsType<AppointmentChangedResult>(result);
        Assert.Equal([fixture.Slot], fixture.Slots.Removed);
        Assert.Equal(SlotStatus.Flagged, fixture.Slot.Status);
    }

    [Fact]
    public async Task A_slot_that_no_longer_exists_does_not_stop_the_cancellation()
    {
        // Schedule revision may hard-delete a slot; the appointment row outlives it.
        var fixture = new Fixture();
        fixture.Slots.Slots.Clear();

        var result = await fixture.Service.CancelAsync(AppointmentId, "Travelling", Desk, "corr-1");

        Assert.IsType<AppointmentChangedResult>(result);
        Assert.Single(fixture.Outbox.Added);
    }

    [Fact]
    public async Task A_slot_that_disagrees_with_the_appointment_is_left_alone()
    {
        // A booked appointment on a Blocked slot should not exist; guessing what to do with it
        // would be worse than leaving it for a person to see.
        var fixture = new Fixture();
        fixture.Slot.Status = SlotStatus.Blocked;

        await fixture.Service.CancelAsync(AppointmentId, "Travelling", Desk, "corr-1");

        Assert.Equal(SlotStatus.Blocked, fixture.Slot.Status);
        Assert.Empty(fixture.Slots.Removed);
    }

    // ── Cancel: concurrency ──────────────────────────────────────────────────

    [Fact]
    public async Task A_lost_race_on_the_slot_is_retried_from_the_top_and_commits_once()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        var result = await fixture.Service.CancelAsync(AppointmentId, "Travelling", Desk, "corr-1");

        Assert.IsType<AppointmentChangedResult>(result);
        Assert.Equal(2, fixture.UnitOfWork.Transactions);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Single(fixture.History.Added);
        Assert.Single(fixture.Outbox.Added);
        Assert.Equal(
            [
                "begin", $"lock appointment {AppointmentId}", $"read slot {SlotId}", "save", "rollback",
                "begin", $"lock appointment {AppointmentId}", $"read slot {SlotId}", "save", "commit"
            ],
            fixture.Log);
    }

    [Fact]
    public async Task Three_lost_races_end_in_a_conflict_and_leave_the_appointment_booked()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.Throw = new ConcurrentUpdateException();

        var result = await fixture.Service.CancelAsync(AppointmentId, "Travelling", Desk, "corr-1");

        Assert.IsType<AppointmentContendedResult>(result);
        Assert.Equal(3, fixture.UnitOfWork.Transactions);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal(AppointmentStatus.Booked, fixture.Appointment.Status);
        Assert.Equal(SlotStatus.Booked, fixture.Slot.Status);
        Assert.Empty(fixture.History.Added);
        Assert.Empty(fixture.Outbox.Added);
    }

    // ── Reschedule: the atomic slot swap (AC1) ───────────────────────────────

    [Fact]
    public async Task Rescheduling_moves_the_appointment_and_swaps_the_slots_in_one_transaction()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        var changed = Assert.IsType<AppointmentChangedResult>(result);
        Assert.Equal(NewSlotId, changed.Appointment.SlotId);

        Assert.Equal(NewSlotId, fixture.Appointment.SlotId);
        Assert.Equal(fixture.NewSlot.StartUtc, fixture.Appointment.StartUtc);
        Assert.Equal(fixture.NewSlot.EndUtc, fixture.Appointment.EndUtc);
        Assert.Equal(fixture.NewSlot.SlotDate, fixture.Appointment.SlotDate);
        Assert.Equal(AppointmentStatus.Booked, fixture.Appointment.Status);
        Assert.Equal("desk@medicore.test", fixture.Appointment.UpdatedBy);

        Assert.Equal(SlotStatus.Available, fixture.Slot.Status);
        Assert.Equal(SlotStatus.Booked, fixture.NewSlot.Status);
        Assert.Equal("desk@medicore.test", fixture.NewSlot.UpdatedBy);

        // Both slots and the appointment in one save, in one transaction that committed.
        Assert.Equal(1, fixture.UnitOfWork.Transactions);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Every_read_and_write_of_a_reschedule_happens_between_begin_and_commit()
    {
        var fixture = new Fixture();

        await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.Equal(
            [
                "begin",
                $"lock appointment {AppointmentId}",
                $"read slot {SlotId}",
                $"lock patient {PatientId}",
                $"read slot {NewSlotId}",
                "check overlap",
                "save",
                "commit"
            ],
            fixture.Log);
    }

    [Fact]
    public async Task A_reschedule_is_recorded_from_the_old_time_to_the_new()
    {
        var fixture = new Fixture();

        await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        var entry = Assert.Single(fixture.History.Added);
        Assert.Equal(AppointmentHistoryAction.Rescheduled, entry.Action);
        Assert.Equal(AppointmentStatus.Booked, entry.FromStatus);
        Assert.Equal(AppointmentStatus.Booked, entry.ToStatus);
        Assert.Equal(SlotId, entry.FromSlotId);
        Assert.Equal(NewSlotId, entry.ToSlotId);
        Assert.Equal(Start, entry.FromStartUtc);
        Assert.Equal(Start.AddDays(1), entry.ToStartUtc);
        Assert.Equal("desk@medicore.test", entry.Actor);
        Assert.Equal(Now, entry.OccurredAtUtc);
    }

    [Fact]
    public async Task A_reschedule_announces_nothing()
    {
        // No appointment.rescheduled contract exists, and nothing downstream consumes one.
        var fixture = new Fixture();

        await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.Empty(fixture.Outbox.Added);
    }

    [Fact]
    public async Task A_new_slot_taken_mid_request_leaves_the_original_untouched()
    {
        // AC1. Another booking took the new slot between our read and our save, and
        // ux_appointments_slot refused ours. The transaction rolls back as a whole.
        var fixture = new Fixture();
        fixture.UnitOfWork.Throw = new SlotAlreadyBookedException();

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.IsType<AppointmentSlotTakenResult>(result);
        Assert.Equal(1, fixture.UnitOfWork.Transactions);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal("rollback", fixture.Log[^1]);
        AssertStillOnTheOriginalSlot(fixture);
        Assert.Equal(SlotStatus.Available, fixture.NewSlot.Status);
    }

    [Fact]
    public async Task A_new_slot_booked_between_our_read_and_our_save_is_reported_taken_after_a_reread()
    {
        // The slot token caught the race. The retry reads the slot as the winner left it.
        var fixture = new Fixture();
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());
        fixture.UnitOfWork.BetweenAttempts = () => fixture.NewSlot.Status = SlotStatus.Booked;

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.IsType<AppointmentSlotTakenResult>(result);
        Assert.Equal(2, fixture.UnitOfWork.Transactions);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        AssertStillOnTheOriginalSlot(fixture);
    }

    [Fact]
    public async Task A_lost_race_that_leaves_the_new_slot_free_moves_the_appointment_on_the_retry()
    {
        // The token only says the row changed — a harmless write, say. The retry finds it free.
        var fixture = new Fixture();
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.IsType<AppointmentChangedResult>(result);
        Assert.Equal(2, fixture.UnitOfWork.Transactions);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Single(fixture.History.Added);
        Assert.Equal(NewSlotId, fixture.Appointment.SlotId);
    }

    [Fact]
    public async Task Three_lost_races_end_in_a_conflict_with_the_appointment_where_it_was()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.Throw = new ConcurrentUpdateException();

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.IsType<AppointmentContendedResult>(result);
        Assert.Equal(3, fixture.UnitOfWork.Transactions);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        AssertStillOnTheOriginalSlot(fixture);
        Assert.Equal(SlotStatus.Available, fixture.NewSlot.Status);
    }

    [Fact]
    public async Task A_new_slot_already_booked_on_the_first_read_is_reported_by_its_status()
    {
        // Not a race we lost, so not "a moment ago": the slot was simply not free.
        var fixture = new Fixture();
        fixture.NewSlot.Status = SlotStatus.Booked;

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.Equal(new AppointmentNewSlotNotAvailableResult(SlotStatus.Booked), result);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        AssertStillOnTheOriginalSlot(fixture);
    }

    // ── Reschedule: what the new slot must be ────────────────────────────────

    [Theory]
    [InlineData(SlotStatus.Blocked)]
    [InlineData(SlotStatus.Flagged)]
    public async Task A_new_slot_that_is_not_available_is_refused(string status)
    {
        var fixture = new Fixture();
        fixture.NewSlot.Status = status;

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.Equal(new AppointmentNewSlotNotAvailableResult(status), result);
        AssertNothingChangedBy(fixture, expectedNewSlotStatus: status);
    }

    [Fact]
    public async Task An_unknown_new_slot_is_not_found()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RescheduleAsync(AppointmentId, Guid.NewGuid(), Desk);

        Assert.IsType<AppointmentNewSlotNotFoundResult>(result);
        AssertNothingChanged(fixture);
    }

    [Fact]
    public async Task Moving_to_the_slot_it_already_has_is_refused()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RescheduleAsync(AppointmentId, SlotId, Desk);

        Assert.IsType<AppointmentNewSlotSameAsCurrentResult>(result);
        AssertNothingChanged(fixture);
    }

    [Fact]
    public async Task A_slot_with_another_doctor_is_refused()
    {
        // Rescheduling moves the time; a different doctor is a cancellation and a new booking.
        var fixture = new Fixture();
        fixture.NewSlot.DoctorId = OtherDoctorId;

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.IsType<AppointmentNewSlotDifferentDoctorResult>(result);
        AssertNothingChanged(fixture);
    }

    [Fact]
    public async Task Nothing_can_move_to_a_doctor_who_is_no_longer_bookable()
    {
        var fixture = new Fixture();
        fixture.Doctors.IsBookable = false;

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.IsType<AppointmentDoctorNotFoundResult>(result);
        AssertNothingChanged(fixture);
    }

    [Fact]
    public async Task A_new_slot_that_has_already_started_is_refused()
    {
        var fixture = new Fixture();
        fixture.NewSlot.StartUtc = Now;
        fixture.NewSlot.EndUtc = Now.AddMinutes(30);

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.Equal(new AppointmentNewSlotInPastResult(Now), result);
        AssertNothingChanged(fixture);
    }

    [Fact]
    public async Task A_new_time_that_clashes_with_another_of_the_patients_appointments_is_refused()
    {
        var fixture = new Fixture();
        var other = new AppointmentEntity
        {
            AppointmentId = Guid.NewGuid(),
            SlotId = Guid.NewGuid(),
            PatientId = PatientId,
            DoctorId = OtherDoctorId,
            StartUtc = fixture.NewSlot.StartUtc.AddMinutes(15),
            EndUtc = fixture.NewSlot.StartUtc.AddMinutes(45),
            Status = AppointmentStatus.Booked
        };
        fixture.Appointments.Appointments.Add(other);

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.Equal(new AppointmentPatientOverlapResult(other.AppointmentId, other.StartUtc, other.EndUtc), result);
        AssertNothingChanged(fixture);
    }

    [Fact]
    public async Task Moving_to_a_time_that_overlaps_the_old_one_is_not_a_clash_with_itself()
    {
        // Fifteen minutes later: the new time overlaps the appointment being moved, which must
        // not count against it.
        var fixture = new Fixture();
        fixture.NewSlot.StartUtc = Start.AddMinutes(15);
        fixture.NewSlot.EndUtc = Start.AddMinutes(45);

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.IsType<AppointmentChangedResult>(result);
    }

    // ── Reschedule: window, status and who may ───────────────────────────────

    [Fact]
    public async Task Rescheduling_inside_the_window_is_refused_like_cancelling()
    {
        var fixture = new Fixture();
        fixture.Appointment.StartUtc = Now.AddHours(5);

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.Equal(new AppointmentInsideCancellationWindowResult(24, Now.AddHours(5)), result);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(SlotStatus.Available, fixture.NewSlot.Status);
    }

    [Fact]
    public async Task A_stranded_booking_can_be_moved_even_inside_the_window()
    {
        // A schedule change flagged its slot: the clinic caused it, so the window does not apply.
        // The flagged slot is removed rather than offered again.
        var fixture = new Fixture();
        fixture.Appointment.StartUtc = Now.AddHours(2);
        fixture.Slot.Status = SlotStatus.Flagged;

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.IsType<AppointmentChangedResult>(result);
        Assert.Equal([fixture.Slot], fixture.Slots.Removed);
        Assert.Equal(SlotStatus.Booked, fixture.NewSlot.Status);
        Assert.Equal(NewSlotId, fixture.Appointment.SlotId);
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task Only_a_booked_appointment_can_be_rescheduled(string status)
    {
        var fixture = new Fixture();
        fixture.Appointment.Status = status;

        var result = await fixture.Service.RescheduleAsync(AppointmentId, NewSlotId, Desk);

        Assert.Equal(new AppointmentInvalidTransitionResult(status, AppointmentHistoryAction.Rescheduled), result);
        AssertNothingChanged(fixture, expectedStatus: status);
    }

    [Fact]
    public async Task A_patient_can_reschedule_their_own_appointment_but_not_anyone_elses()
    {
        var own = new Fixture();
        var someoneElses = new Fixture();

        var ownResult = await own.Service.RescheduleAsync(
            AppointmentId, NewSlotId, new AppointmentCaller("patient", PatientId));
        var otherResult = await someoneElses.Service.RescheduleAsync(
            AppointmentId, NewSlotId, new AppointmentCaller("patient", OtherPatientId));

        Assert.IsType<AppointmentChangedResult>(ownResult);
        Assert.IsType<AppointmentNotFoundResult>(otherResult);
        AssertNothingChanged(someoneElses);
    }

    [Fact]
    public async Task Rescheduling_an_unknown_appointment_is_not_found()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RescheduleAsync(Guid.NewGuid(), NewSlotId, Desk);

        Assert.IsType<AppointmentNotFoundResult>(result);
        AssertNothingChanged(fixture);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AssertNothingChangedBy(Fixture fixture, string expectedNewSlotStatus)
    {
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Equal(expectedNewSlotStatus, fixture.NewSlot.Status);
        Assert.Null(fixture.NewSlot.UpdatedBy);
        AssertStillOnTheOriginalSlot(fixture);
    }

    /// <summary>
    /// A refusal leaves everything as it was: no save, no history, no event, and the appointment
    /// and its slot untouched.
    /// </summary>
    private static void AssertNothingChanged(Fixture fixture, string expectedStatus = AppointmentStatus.Booked)
    {
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.History.Added);
        Assert.Empty(fixture.Outbox.Added);
        Assert.Empty(fixture.Slots.Removed);
        Assert.Equal(expectedStatus, fixture.Appointment.Status);
        Assert.Null(fixture.Appointment.UpdatedBy);
        Assert.Equal(SlotStatus.Booked, fixture.Slot.Status);
        Assert.Null(fixture.Slot.UpdatedBy);
        Assert.Equal(SlotStatus.Available, fixture.NewSlot.Status);
        Assert.Null(fixture.NewSlot.UpdatedBy);
    }

    /// <summary>
    /// AC1's "leaving the original untouched": still on its old slot, at its old time, still
    /// holding that slot, and the new slot still free.
    /// </summary>
    private static void AssertStillOnTheOriginalSlot(Fixture fixture)
    {
        Assert.Equal(SlotId, fixture.Appointment.SlotId);
        Assert.Equal(Start, fixture.Appointment.StartUtc);
        Assert.Equal(Start.AddMinutes(30), fixture.Appointment.EndUtc);
        Assert.Equal(AppointmentStatus.Booked, fixture.Appointment.Status);
        Assert.Equal(SlotStatus.Booked, fixture.Slot.Status);
        Assert.Empty(fixture.History.Added);
        Assert.Empty(fixture.Outbox.Added);
    }

    /// <summary>
    /// One booked appointment on one booked slot, three days out. The fakes behave like the
    /// database across a rollback: whatever an attempt changed or staged is put back, so a retry
    /// reads what a fresh transaction would.
    /// </summary>
    private sealed class Fixture
    {
        private List<(object Entity, Dictionary<string, object?> Values)> _snapshot = [];

        public Fixture(int windowHours = 24)
        {
            Slot = new Slot
            {
                SlotId = SlotId,
                DoctorId = DoctorId,
                StartUtc = Start,
                EndUtc = Start.AddMinutes(30),
                SlotDate = ColomboTime.ToColomboDate(Start),
                DurationMinutes = 30,
                Status = SlotStatus.Booked
            };
            Appointment = new AppointmentEntity
            {
                AppointmentId = AppointmentId,
                SlotId = SlotId,
                PatientId = PatientId,
                DoctorId = DoctorId,
                StartUtc = Start,
                EndUtc = Start.AddMinutes(30),
                SlotDate = ColomboTime.ToColomboDate(Start),
                DurationMinutes = 30,
                Status = AppointmentStatus.Booked,
                CreatedBy = "desk"
            };

            // A day after the original, same doctor, free: where a reschedule moves to.
            NewSlot = new Slot
            {
                SlotId = NewSlotId,
                DoctorId = DoctorId,
                StartUtc = Start.AddDays(1),
                EndUtc = Start.AddDays(1).AddMinutes(30),
                SlotDate = ColomboTime.ToColomboDate(Start.AddDays(1)),
                DurationMinutes = 30,
                Status = SlotStatus.Available
            };

            Appointments = new FakeAppointmentRepository(Log, Appointment);
            Slots = new FakeSlotRepository(Log, Slot, NewSlot);
            Doctors = new FakeDoctorCacheRepository();
            Outbox = new FakeOutboxMessageRepository();
            History = new FakeAppointmentHistoryRepository();
            UnitOfWork = new FakeUnitOfWork(Log, onBegin: TakeSnapshot, onRollback: Restore);

            Service = new AppointmentLifecycleService(
                Appointments,
                Slots,
                Doctors,
                Outbox,
                History,
                UnitOfWork,
                new FixedTimeProvider(Now),
                Options.Create(new CancellationPolicyOptions { WindowHours = windowHours }));
        }

        /// <summary>Every step of every attempt, in order, across the fakes.</summary>
        public List<string> Log { get; } = [];

        public AppointmentEntity Appointment { get; }

        public Slot Slot { get; }

        public Slot NewSlot { get; }

        public FakeAppointmentRepository Appointments { get; }

        public FakeDoctorCacheRepository Doctors { get; }

        public FakeSlotRepository Slots { get; }

        public FakeOutboxMessageRepository Outbox { get; }

        public FakeAppointmentHistoryRepository History { get; }

        public FakeUnitOfWork UnitOfWork { get; }

        public AppointmentLifecycleService Service { get; }

        private void TakeSnapshot()
        {
            _snapshot = Appointments.Appointments.Cast<object>()
                .Concat(Slots.Slots.Values)
                .Select(entity => (entity, entity.GetType().GetProperties()
                    .Where(property => property.CanWrite)
                    .ToDictionary(property => property.Name, property => property.GetValue(entity))))
                .ToList();
        }

        private void Restore()
        {
            foreach (var (entity, values) in _snapshot)
            {
                foreach (var (name, value) in values)
                {
                    entity.GetType().GetProperty(name)!.SetValue(entity, value);
                }
            }

            Outbox.Added.Clear();
            History.Added.Clear();
            Slots.Removed.Clear();
        }
    }

    private sealed class FakeAppointmentRepository : IAppointmentRepository
    {
        private readonly List<string> _log;

        public FakeAppointmentRepository(List<string> log, params AppointmentEntity[] appointments)
        {
            _log = log;
            Appointments = [.. appointments];
        }

        public List<AppointmentEntity> Appointments { get; }

        public Task<AppointmentEntity?> GetTrackedForUpdateAsync(
            Guid appointmentId, CancellationToken cancellationToken = default)
        {
            _log.Add($"lock appointment {appointmentId}");
            return Task.FromResult(Appointments.FirstOrDefault(a => a.AppointmentId == appointmentId));
        }

        public Task LockPatientAsync(Guid patientId, CancellationToken cancellationToken = default)
        {
            _log.Add($"lock patient {patientId}");
            return Task.CompletedTask;
        }

        // The same half-open predicate and exclusion the real repository applies, so the rules
        // are exercised rather than assumed.
        public Task<AppointmentEntity?> FindPatientOverlapAsync(
            Guid patientId, DateTime startUtc, DateTime endUtc, Guid? excludeAppointmentId = null,
            CancellationToken cancellationToken = default)
        {
            _log.Add("check overlap");
            return Task.FromResult(Appointments
                .Where(a =>
                    a.PatientId == patientId
                    && a.Status == AppointmentStatus.Booked
                    && a.StartUtc < endUtc
                    && a.EndUtc > startUtc
                    && a.AppointmentId != excludeAppointmentId)
                .OrderBy(a => a.StartUtc)
                .FirstOrDefault());
        }

        public Task AddAsync(AppointmentEntity appointment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes never create appointments.");

        public Task<IReadOnlyList<AppointmentListing>> ListAsync(
            Guid? doctorId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes never list.");

        public Task<IReadOnlyList<AppointmentListing>> ListUpcomingForPatientAsync(
            Guid patientId, DateTime nowUtc, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes never list.");

        public Task<AppointmentEntity?> GetByAppointmentIdAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes read through the row lock, never untracked.");
    }

    private sealed class FakeSlotRepository : ISlotRepository
    {
        private readonly List<string> _log;

        public FakeSlotRepository(List<string> log, params Slot[] slots)
        {
            _log = log;
            Slots = slots.ToDictionary(slot => slot.SlotId);
        }

        public Dictionary<Guid, Slot> Slots { get; }

        public List<Slot> Removed { get; } = [];

        public Task<Slot?> GetTrackedBySlotIdAsync(Guid slotId, CancellationToken cancellationToken = default)
        {
            _log.Add($"read slot {slotId}");
            return Task.FromResult(Slots.GetValueOrDefault(slotId));
        }

        public void RemoveRange(IReadOnlyCollection<Slot> slots) => Removed.AddRange(slots);

        public Task<IReadOnlyList<Slot>> GetTrackedForDoctorBetweenAsync(
            Guid doctorId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes never reconcile a schedule.");

        public Task AddRangeAsync(IReadOnlyCollection<Slot> slots, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes never create slots.");

        public Task<IReadOnlyList<Slot>> GetAvailableAsync(
            Guid doctorId, DateOnly from, DateOnly to, DateTime nowUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes work from slot ids, not a listing.");

        public Task<IReadOnlyList<Slot>> GetFlaggedAsync(
            Guid? doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes never read the attention list.");
    }

    private sealed class FakeDoctorCacheRepository : IDoctorCacheRepository
    {
        public bool IsBookable { get; set; } = true;

        public Task<DoctorCache?> GetActiveAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsBookable
                ? new DoctorCache { DoctorId = doctorId, FullName = "Nimal Perera", IsActive = true }
                : null);

        public Task<DoctorCache?> GetTrackedByDoctorIdAsync(
            Guid doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes never write the cache.");

        public Task<IReadOnlyList<DoctorCache>> ListActiveAsync(
            string? specialization, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes work from one doctor.");

        public Task<IReadOnlyList<string>> ListSpecializationsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes never list specializations.");

        public Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes never write the cache.");
    }

    private sealed class FakeOutboxMessageRepository : IOutboxMessageRepository
    {
        public List<OutboxMessage> Added { get; } = [];

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Added.Add(message);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedBatchAsync(
            int batchSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only the dispatcher drains the outbox.");

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes commit through the unit of work.");
    }

    private sealed class FakeAppointmentHistoryRepository : IAppointmentHistoryRepository
    {
        public List<AppointmentHistoryEntry> Added { get; } = [];

        public Task AddAsync(AppointmentHistoryEntry entry, CancellationToken cancellationToken = default)
        {
            Added.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AppointmentHistoryEntry>> ListForAppointmentAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Changes never read history.");
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        private readonly List<string> _log;
        private readonly Action _onBegin;
        private readonly Action _onRollback;

        public FakeUnitOfWork(List<string> log, Action onBegin, Action onRollback)
        {
            _log = log;
            _onBegin = onBegin;
            _onRollback = onRollback;
        }

        public int SaveCount { get; private set; }

        public int Transactions { get; private set; }

        public int Commits { get; private set; }

        /// <summary>Thrown by every save. <see cref="Outcomes"/> takes precedence while it lasts.</summary>
        public Exception? Throw { get; set; }

        /// <summary>What each save does in turn; a null entry means that save succeeds.</summary>
        public Queue<Exception?> Outcomes { get; } = new();

        /// <summary>
        /// Runs after a rollback has restored the fakes — another writer's change landing between
        /// this attempt and the next.
        /// </summary>
        public Action? BetweenAttempts { get; set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            _log.Add("save");

            var outcome = Outcomes.TryDequeue(out var next) ? next : Throw;
            return outcome is null ? Task.CompletedTask : Task.FromException(outcome);
        }

        public async Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default)
        {
            Transactions++;
            _log.Add("begin");
            _onBegin();

            try
            {
                var result = await work(cancellationToken);
                Commits++;
                _log.Add("commit");
                return result;
            }
            catch
            {
                _log.Add("rollback");
                _onRollback();
                BetweenAttempts?.Invoke();
                throw;
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTime now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
