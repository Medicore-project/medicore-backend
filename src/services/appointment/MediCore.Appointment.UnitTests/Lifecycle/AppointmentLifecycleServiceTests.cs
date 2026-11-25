using System.Text.Json;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using MediCore.Appointment.UnitTests.Support;
using Microsoft.Extensions.Options;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.UnitTests.Lifecycle;

/// <summary>
/// SCRUM-36 — cancel, reschedule, complete and no-show, isolated from the database.
/// </summary>
/// <remarks>
/// The waitlist hand-off is a separate service (<see cref="IWaitlistOfferer"/>), so here it is a
/// pure <b>mock</b>: these tests only care that it is <i>told</i> about a released slot, not what it
/// does with it. Its own behaviour is unit-tested in <c>WaitlistOffererTests</c>.
/// </remarks>
[Trait(TestCategories.Category, TestCategories.Unit)]
[Trait(TestCategories.Feature, TestCategories.Lifecycle)]
public sealed class AppointmentLifecycleServiceTests
{
    private const string CorrelationId = "corr-lifecycle-1";

    private static readonly AppointmentCaller FrontDesk = new("reception@medicore.test", IsFrontDesk: true);
    private static readonly AppointmentCaller TreatingDoctor = new("dr.silva@medicore.test", StaffId: Ids.Doctor);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ISlotRepository> _slots = new();
    private readonly Mock<IDoctorCacheRepository> _doctors = new();
    private readonly Mock<IOutboxMessageRepository> _outbox = new();
    private readonly Mock<IAppointmentHistoryRepository> _history = new();
    private readonly Mock<IWaitlistOfferer> _waitlist = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public AppointmentLifecycleServiceTests()
    {
        _unitOfWork.SetupTransaction<AppointmentChangeResult>();
        _doctors
            .Setup(d => d.GetActiveAsync(Ids.Doctor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Doctors.Active());
    }

    private AppointmentLifecycleService CreateService(int windowHours = 24, TimeProvider? clock = null) => new(
        _appointments.Object,
        _slots.Object,
        _doctors.Object,
        _outbox.Object,
        _history.Object,
        _waitlist.Object,
        _unitOfWork.Object,
        clock ?? TestClock.Frozen(),
        Options.Create(new CancellationPolicyOptions { WindowHours = windowHours }));

    private AppointmentEntity GivenAppointment(AppointmentEntity appointment)
    {
        _appointments
            .Setup(a => a.GetTrackedForUpdateAsync(appointment.AppointmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);
        return appointment;
    }

    private Slot GivenSlot(Slot slot)
    {
        _slots
            .Setup(s => s.GetTrackedBySlotIdAsync(slot.SlotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(slot);
        return slot;
    }

    private void VerifyNothingWasSaved()
    {
        _history.Verify(h => h.AddAsync(It.IsAny<AppointmentHistoryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _outbox.Verify(o => o.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        _waitlist.Verify(w => w.OfferAsync(It.IsAny<Slot>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ══ Cancel ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(24, 24 * 60 + 1, true)]  // one minute outside the window
    [InlineData(24, 24 * 60, false)]     // exactly on the boundary -> inside (now >= start - window)
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task Cancellation_respects_the_configurable_window(
        int windowHours, int minutesUntilStart, bool allowed)
    {
        var appointment = GivenAppointment(AppointmentBuilder.AnAppointment()
            .StartingIn(TimeSpan.FromMinutes(minutesUntilStart)).Build());
        var slot = GivenSlot(SlotBuilder.ASlot().WithStatus(SlotStatus.Booked).StartingAt(appointment.StartUtc).Build());

        var result = await CreateService(windowHours).CancelAsync(
            appointment.AppointmentId, "Travelling", FrontDesk, CorrelationId);

        if (allowed)
        {
            Assert.IsType<AppointmentChangedResult>(result);
            Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
            Assert.Equal(SlotStatus.Available, slot.Status);
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            var refused = Assert.IsType<AppointmentInsideCancellationWindowResult>(result);
            Assert.Equal(Math.Max(0, windowHours), refused.WindowHours);
            Assert.Equal(appointment.StartUtc, refused.StartUtc);
            Assert.Equal(AppointmentStatus.Booked, appointment.Status);
            VerifyNothingWasSaved();
        }
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task A_successful_cancel_publishes_the_event_records_history_and_offers_the_slot_to_the_waitlist()
    {
        var appointment = GivenAppointment(AppointmentBuilder.AnAppointment().Build());
        var slot = GivenSlot(SlotBuilder.ASlot().WithStatus(SlotStatus.Booked).Build());

        OutboxMessage? published = null;
        _outbox
            .Setup(o => o.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxMessage, CancellationToken>((message, _) => published = message)
            .Returns(Task.CompletedTask);

        await CreateService().CancelAsync(
            appointment.AppointmentId, "   Patient is travelling   ", FrontDesk, CorrelationId);

        // Reason is trimmed before it is stored or published.
        Assert.NotNull(published);
        Assert.Equal("appointment.cancelled", published!.EventType);
        using (var payload = JsonDocument.Parse(published.Payload))
        {
            Assert.Equal("Patient is travelling", payload.RootElement.GetProperty("reason").GetString());
        }

        _history.Verify(h => h.AddAsync(
                It.Is<AppointmentHistoryEntry>(e =>
                    e.Action == AppointmentHistoryAction.Cancelled
                    && e.FromStatus == AppointmentStatus.Booked
                    && e.ToStatus == AppointmentStatus.Cancelled
                    && e.Reason == "Patient is travelling"
                    && e.Actor == FrontDesk.Actor
                    && e.OccurredAtUtc == TestClock.Now),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // SCRUM-37: the released slot — the very same instance — is handed to the waitlist.
        _waitlist.Verify(w => w.OfferAsync(
                It.Is<Slot>(s => ReferenceEquals(s, slot) && s.Status == SlotStatus.Available),
                TestClock.Now,
                FrontDesk.Actor,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task The_waitlist_offer_is_staged_before_the_single_save()
    {
        GivenAppointment(AppointmentBuilder.AnAppointment().Build());
        GivenSlot(SlotBuilder.ASlot().WithStatus(SlotStatus.Booked).Build());

        // A strict MockSequence: the save must come after the offer, otherwise the offer would be
        // a second transaction and the "cancel + re-offer" pair would not be atomic.
        var sequence = new MockSequence();
        var waitlist = new Mock<IWaitlistOfferer>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
        unitOfWork.SetupTransaction<AppointmentChangeResult>();
        waitlist.InSequence(sequence)
            .Setup(w => w.OfferAsync(It.IsAny<Slot>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WaitlistEntry?)null);
        unitOfWork.InSequence(sequence)
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new AppointmentLifecycleService(
            _appointments.Object, _slots.Object, _doctors.Object, _outbox.Object, _history.Object,
            waitlist.Object, unitOfWork.Object, TestClock.Frozen(),
            Options.Create(new CancellationPolicyOptions()));

        var result = await service.CancelAsync(Ids.Appointment, "Reason", FrontDesk, CorrelationId);

        Assert.IsType<AppointmentChangedResult>(result);
        waitlist.VerifyAll();
        unitOfWork.VerifyAll();
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task A_closed_appointment_cannot_be_cancelled(string status)
    {
        var appointment = GivenAppointment(AppointmentBuilder.AnAppointment().WithStatus(status).Build());

        var result = await CreateService().CancelAsync(appointment.AppointmentId, "x", FrontDesk, CorrelationId);

        var invalid = Assert.IsType<AppointmentInvalidTransitionResult>(result);
        Assert.Equal(status, invalid.CurrentStatus);
        Assert.Equal(AppointmentHistoryAction.Cancelled, invalid.Action);
        _slots.Verify(s => s.GetTrackedBySlotIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingWasSaved();
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task A_patient_cannot_cancel_someone_elses_appointment_and_is_not_told_it_exists()
    {
        GivenAppointment(AppointmentBuilder.AnAppointment().ForPatient(Ids.Patient).Build());
        var intruder = new AppointmentCaller("patient-2", PatientId: Ids.OtherPatient);

        var result = await CreateService().CancelAsync(Ids.Appointment, "x", intruder, CorrelationId);

        Assert.IsType<AppointmentNotFoundResult>(result); // 404, not 403 — no existence leak
        VerifyNothingWasSaved();
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task Cancelling_off_a_flagged_slot_deletes_the_orphan_and_offers_nothing()
    {
        var appointment = GivenAppointment(AppointmentBuilder.AnAppointment().Build());
        var flagged = GivenSlot(SlotBuilder.ASlot().WithStatus(SlotStatus.Flagged).Build());

        var result = await CreateService().CancelAsync(appointment.AppointmentId, "Doctor left", FrontDesk, CorrelationId);

        Assert.IsType<AppointmentChangedResult>(result);
        _slots.Verify(s => s.RemoveRange(It.Is<IReadOnlyCollection<Slot>>(c => c.Single() == flagged)), Times.Once);
        _waitlist.Verify(w => w.OfferAsync(It.IsAny<Slot>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ══ Reschedule ════════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task Rescheduling_swaps_the_slots_atomically_and_keeps_the_appointment_id()
    {
        var appointment = GivenAppointment(AppointmentBuilder.AnAppointment().Build());
        var oldSlot = GivenSlot(SlotBuilder.ASlot().WithStatus(SlotStatus.Booked).StartingAt(appointment.StartUtc).Build());
        var newSlot = GivenSlot(SlotBuilder.ASlot().WithId(Ids.NewSlot).StartingIn(TimeSpan.FromDays(5)).Build());

        var result = await CreateService().RescheduleAsync(appointment.AppointmentId, Ids.NewSlot, FrontDesk);

        var changed = Assert.IsType<AppointmentChangedResult>(result);
        Assert.Equal(Ids.Appointment, changed.Appointment.AppointmentId);
        Assert.Equal(Ids.NewSlot, appointment.SlotId);
        Assert.Equal(newSlot.StartUtc, appointment.StartUtc);
        Assert.Equal(SlotStatus.Available, oldSlot.Status);
        Assert.Equal(SlotStatus.Booked, newSlot.Status);

        _history.Verify(h => h.AddAsync(
                It.Is<AppointmentHistoryEntry>(e =>
                    e.Action == AppointmentHistoryAction.Rescheduled
                    && e.FromSlotId == Ids.Slot
                    && e.ToSlotId == Ids.NewSlot),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _waitlist.Verify(w => w.OfferAsync(oldSlot, TestClock.Now, FrontDesk.Actor, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // A reschedule keeps the same appointment and tariff, so no Kafka event is staged.
        _outbox.VerifyNoOtherCalls();

        // The patient's own appointment must not count as a clash with its new time.
        _appointments.Verify(a => a.FindPatientOverlapAsync(
                Ids.Patient, newSlot.StartUtc, newSlot.EndUtc, Ids.Appointment, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Scenario name -> how to spoil the target slot. Kept out of the TheoryData so every row stays
    // xUnit-serializable and shows up as its own named test case in the runner.
    private static readonly IReadOnlyDictionary<string, Action<Slot>> SlotSpoilers =
        new Dictionary<string, Action<Slot>>
        {
            ["another doctor's slot"] = s => s.DoctorId = Ids.OtherDoctor,
            ["already booked"] = s => s.Status = SlotStatus.Booked,
            ["already started"] = s => { s.StartUtc = TestClock.Now; s.EndUtc = TestClock.Now.AddMinutes(30); },
        };

    public static TheoryData<string, Type> RescheduleGuards => new()
    {
        { "another doctor's slot", typeof(AppointmentNewSlotDifferentDoctorResult) },
        { "already booked", typeof(AppointmentNewSlotNotAvailableResult) },
        { "already started", typeof(AppointmentNewSlotInPastResult) },
    };

    [Theory]
    [MemberData(nameof(RescheduleGuards))]
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task Rescheduling_to_an_unsuitable_slot_is_refused_and_nothing_moves(
        string scenario, Type expectedResult)
    {
        var appointment = GivenAppointment(AppointmentBuilder.AnAppointment().Build());
        var oldSlot = GivenSlot(SlotBuilder.ASlot().WithStatus(SlotStatus.Booked).Build());
        var newSlot = SlotBuilder.ASlot().WithId(Ids.NewSlot).StartingIn(TimeSpan.FromDays(5)).Build();
        SlotSpoilers[scenario](newSlot);
        GivenSlot(newSlot);

        var result = await CreateService().RescheduleAsync(appointment.AppointmentId, Ids.NewSlot, FrontDesk);

        Assert.True(expectedResult == result.GetType(), $"{scenario}: got {result.GetType().Name}");
        Assert.Equal(Ids.Slot, appointment.SlotId);
        Assert.Equal(SlotStatus.Booked, oldSlot.Status);
        VerifyNothingWasSaved();
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task Rescheduling_onto_the_current_slot_is_rejected_without_touching_the_repositories()
    {
        GivenAppointment(AppointmentBuilder.AnAppointment().Build());

        var result = await CreateService().RescheduleAsync(Ids.Appointment, Ids.Slot, FrontDesk);

        Assert.IsType<AppointmentNewSlotSameAsCurrentResult>(result);
        _slots.VerifyNoOtherCalls();
        _appointments.Verify(a => a.LockPatientAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task A_stranded_booking_on_a_flagged_slot_may_move_even_inside_the_window()
    {
        // Starts in 2 hours — normally inside the 24h window — but its slot was flagged by a
        // schedule change, so the clinic must be able to rehome it.
        var appointment = GivenAppointment(AppointmentBuilder.AnAppointment().StartingIn(TimeSpan.FromHours(2)).Build());
        var flagged = GivenSlot(SlotBuilder.ASlot().WithStatus(SlotStatus.Flagged).Build());
        GivenSlot(SlotBuilder.ASlot().WithId(Ids.NewSlot).StartingIn(TimeSpan.FromDays(2)).Build());

        var result = await CreateService().RescheduleAsync(appointment.AppointmentId, Ids.NewSlot, FrontDesk);

        Assert.IsType<AppointmentChangedResult>(result);
        _slots.Verify(s => s.RemoveRange(It.Is<IReadOnlyCollection<Slot>>(c => c.Contains(flagged))), Times.Once);
    }

    // ══ Complete & No-show: who may do it, and when ═══════════════════════════

    private static readonly IReadOnlyDictionary<string, AppointmentCaller> Callers =
        new Dictionary<string, AppointmentCaller>
        {
            ["the treating doctor"] = TreatingDoctor,
            ["front desk"] = FrontDesk,
        };

    public static TheoryData<string, bool> CompletionCallers => new()
    {
        { "the treating doctor", true },
        { "front desk", false },
    };

    [Theory]
    [MemberData(nameof(CompletionCallers))]
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task Only_the_treating_doctor_can_complete_a_visit(string who, bool allowed)
    {
        var caller = Callers[who];
        GivenAppointment(AppointmentBuilder.AnAppointment().StartingIn(TimeSpan.FromMinutes(-10)).Build());

        var result = await CreateService().CompleteAsync(Ids.Appointment, "BP normal", caller, CorrelationId);

        if (allowed)
        {
            Assert.IsType<AppointmentChangedResult>(result);
        }
        else
        {
            Assert.True(result is AppointmentNotYourAppointmentResult, $"{who}: got {result.GetType().Name}");
            VerifyNothingWasSaved();
        }
    }

    [Theory]
    [InlineData(0, true)]    // boundary: the visit starts now
    [InlineData(1, false)]   // starts in a minute
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task A_visit_cannot_be_completed_before_it_starts(int startOffsetMinutes, bool allowed)
    {
        GivenAppointment(AppointmentBuilder.AnAppointment().StartingIn(TimeSpan.FromMinutes(startOffsetMinutes)).Build());

        var result = await CreateService().CompleteAsync(Ids.Appointment, "notes", TreatingDoctor, CorrelationId);

        Assert.Equal(allowed, result is AppointmentChangedResult);
        if (!allowed)
        {
            Assert.IsType<AppointmentNotStartedYetResult>(result);
        }
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task Completing_publishes_appointment_completed_for_Patient_and_Billing()
    {
        GivenAppointment(AppointmentBuilder.AnAppointment().StartingIn(TimeSpan.FromMinutes(-5)).Build());

        await CreateService().CompleteAsync(Ids.Appointment, "  Prescribed rest  ", TreatingDoctor, CorrelationId);

        _outbox.Verify(o => o.AddAsync(
                It.Is<OutboxMessage>(m =>
                    m.EventType == "appointment.completed"
                    && m.Payload.Contains("\"notes\":\"Prescribed rest\"")
                    && m.Payload.Contains(Ids.Patient.ToString())),
                It.IsAny<CancellationToken>()),
            Times.Once);
        // Completing does not free the slot, so nothing is offered to the waitlist.
        _waitlist.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, null, true)]                // front desk
    [InlineData(false, "doctor", true)]           // treating doctor
    [InlineData(false, "other-doctor", false)]
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task No_show_can_be_marked_by_front_desk_or_the_treating_doctor(
        bool isFrontDesk, string? staff, bool allowed)
    {
        Guid? staffId = staff switch { "doctor" => Ids.Doctor, "other-doctor" => Ids.OtherDoctor, _ => null };
        var caller = new AppointmentCaller("actor", StaffId: staffId, IsFrontDesk: isFrontDesk);
        GivenAppointment(AppointmentBuilder.AnAppointment().StartingIn(TimeSpan.FromHours(-2)).Build());

        var result = await CreateService().MarkNoShowAsync(Ids.Appointment, caller);

        Assert.Equal(allowed, result is AppointmentChangedResult);
        // No-show is internal bookkeeping: unlike cancel/complete it publishes no event.
        _outbox.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(-30, true)]  // boundary: ends exactly now
    [InlineData(-29, false)] // still running
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task No_show_waits_until_the_slot_has_ended(int startOffsetMinutes, bool allowed)
    {
        GivenAppointment(AppointmentBuilder.AnAppointment().StartingIn(TimeSpan.FromMinutes(startOffsetMinutes)).Build());

        var result = await CreateService().MarkNoShowAsync(Ids.Appointment, FrontDesk);

        if (allowed)
        {
            Assert.IsType<AppointmentChangedResult>(result);
        }
        else
        {
            Assert.IsType<AppointmentNotEndedYetResult>(result);
        }
    }

    // ══ Concurrency ═══════════════════════════════════════════════════════════

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task Changes_that_keep_losing_races_end_as_contended_after_three_attempts()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var transaction = unitOfWork.SetupTransaction<AppointmentChangeResult>(failFirst: int.MaxValue);
        var service = new AppointmentLifecycleService(
            _appointments.Object, _slots.Object, _doctors.Object, _outbox.Object, _history.Object,
            _waitlist.Object, unitOfWork.Object, TestClock.Frozen(),
            Options.Create(new CancellationPolicyOptions()));

        var result = await service.CancelAsync(Ids.Appointment, "x", FrontDesk, CorrelationId);

        Assert.IsType<AppointmentContendedResult>(result);
        Assert.Equal(3, transaction.Attempts);
    }
}
