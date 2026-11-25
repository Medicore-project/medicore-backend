using System.Text.Json;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Services;
using MediCore.Appointment.UnitTests.Support;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.UnitTests.Booking;

/// <summary>
/// SCRUM-34 / SCRUM-35 — booking a slot, isolated from Postgres and Kafka.
/// </summary>
/// <remarks>
/// <para>System under test: <see cref="AppointmentBookingService"/>.</para>
/// <para>
/// Test doubles (all Moq):
/// <list type="bullet">
/// <item><b>Stubs</b> feed canned answers in — the slot, the doctor, "no clash".</item>
/// <item><b>Mocks</b> are interrogated afterwards with <c>Verify</c> — was the outbox row written,
/// was the transaction committed exactly once, was nothing saved on a rejection.</item>
/// <item><see cref="IUnitOfWork"/> is a pass-through fake transaction that can be told to lose
/// concurrency races, which lets the SCRUM-35 retry loop be exercised without real threads.</item>
/// </list>
/// </para>
/// xUnit builds a new instance of this class for every test, so every test gets fresh mocks.
/// </remarks>
[Trait(TestCategories.Category, TestCategories.Unit)]
[Trait(TestCategories.Feature, TestCategories.Booking)]
public sealed class AppointmentBookingServiceTests
{
    private const string Actor = "reception@medicore.test";
    private const string CorrelationId = "corr-booking-1";

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ISlotRepository> _slots = new();
    private readonly Mock<IDoctorCacheRepository> _doctors = new();
    private readonly Mock<IOutboxMessageRepository> _outbox = new();
    private readonly Mock<IAppointmentHistoryRepository> _history = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly TransactionProbe _transaction;

    public AppointmentBookingServiceTests()
    {
        // Default world: an active cardiologist and a pass-through transaction that always wins.
        _doctors
            .Setup(d => d.GetActiveAsync(Ids.Doctor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Doctors.Active());

        _transaction = _unitOfWork.SetupTransaction<BookingResult>();
    }

    private AppointmentBookingService CreateService(TimeProvider? clock = null) => new(
        _appointments.Object,
        _slots.Object,
        _doctors.Object,
        _outbox.Object,
        _history.Object,
        _unitOfWork.Object,
        clock ?? TestClock.Frozen());

    private void GivenSlot(Slot slot) =>
        _slots
            .Setup(s => s.GetTrackedBySlotIdAsync(slot.SlotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(slot);

    private Task<BookingResult> BookAsync(string? serviceCode = null) =>
        CreateService().BookAsync(Ids.Slot, Ids.Patient, serviceCode, Actor, CorrelationId);

    private void VerifyNothingWasWritten()
    {
        _appointments.Verify(a => a.AddAsync(It.IsAny<AppointmentEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _outbox.Verify(o => o.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        _history.Verify(h => h.AddAsync(It.IsAny<AppointmentHistoryEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Happy path: behaviour verification ────────────────────────────────────

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task Booking_a_free_future_slot_writes_appointment_event_and_history_in_one_save()
    {
        var slot = SlotBuilder.ASlot().Build();
        GivenSlot(slot);

        AppointmentEntity? added = null;
        _appointments
            .Setup(a => a.AddAsync(It.IsAny<AppointmentEntity>(), It.IsAny<CancellationToken>()))
            .Callback<AppointmentEntity, CancellationToken>((captured, _) => added = captured)
            .Returns(Task.CompletedTask);

        var result = await BookAsync();

        var created = Assert.IsType<BookingCreatedResult>(result);
        Assert.NotNull(added);
        var appointment = added!;
        Assert.Equal(appointment.AppointmentId, created.Appointment.AppointmentId);
        Assert.Equal(Ids.Patient, appointment.PatientId);
        Assert.Equal(slot.StartUtc, appointment.StartUtc);
        Assert.Equal(AppointmentStatus.Booked, appointment.Status);

        // The slot entity handed out by the stub was mutated in place (EF change tracking would save it).
        Assert.Equal(SlotStatus.Booked, slot.Status);
        Assert.Equal(Actor, slot.UpdatedBy);

        // Transactional outbox: the Kafka event is a row staged next to the appointment, not a publish.
        _outbox.Verify(o => o.AddAsync(
                It.Is<OutboxMessage>(m =>
                    m.Topic == "appointment-events"
                    && m.EventType == "appointment.booked"
                    && m.EventKey == appointment.AppointmentId.ToString()
                    && m.CorrelationId == CorrelationId),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _history.Verify(h => h.AddAsync(
                It.Is<AppointmentHistoryEntry>(e =>
                    e.Action == AppointmentHistoryAction.Booked
                    && e.AppointmentId == appointment.AppointmentId
                    && e.Actor == Actor),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, _transaction.Attempts);
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task The_patient_lock_is_taken_before_anything_is_read_and_the_save_comes_last()
    {
        // Interaction-order verification: callbacks record the sequence of calls across mocks.
        var calls = new List<string>();
        var slot = SlotBuilder.ASlot().Build();

        _appointments
            .Setup(a => a.LockPatientAsync(Ids.Patient, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("lock-patient"))
            .Returns(Task.CompletedTask);
        _slots
            .Setup(s => s.GetTrackedBySlotIdAsync(Ids.Slot, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("read-slot"))
            .ReturnsAsync(slot);
        _appointments
            .Setup(a => a.FindPatientOverlapAsync(Ids.Patient, slot.StartUtc, slot.EndUtc, null, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("overlap-check"))
            .ReturnsAsync((AppointmentEntity?)null);
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("save"))
            .Returns(Task.CompletedTask);

        await BookAsync();

        Assert.Equal(new[] { "lock-patient", "read-slot", "overlap-check", "save" }, calls);
    }

    // ── Guards: parameterized + "nothing was written" verification ────────────

    public static TheoryData<string> UnbookableSlotStatuses => new()
    {
        SlotStatus.Booked,
        SlotStatus.Blocked,
    };

    [Theory]
    [MemberData(nameof(UnbookableSlotStatuses))]
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task A_slot_that_is_not_Available_is_refused_and_nothing_is_saved(string status)
    {
        GivenSlot(SlotBuilder.ASlot().WithStatus(status).Build());

        var result = await BookAsync();

        var refused = Assert.IsType<BookingSlotNotAvailableResult>(result);
        Assert.Equal(status, refused.CurrentStatus);
        VerifyNothingWasWritten();
    }

    [Theory]
    [InlineData(-1, false)]       // a minute ago
    [InlineData(0, false)]        // boundary: starts exactly now -> already in the past (StartUtc <= now)
    [InlineData(1, true)]         // boundary + 1 minute
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task Past_slot_rule_is_inclusive_of_the_current_instant(int minutesFromNow, bool shouldBook)
    {
        var slot = SlotBuilder.ASlot().StartingIn(TimeSpan.FromMinutes(minutesFromNow)).Build();
        GivenSlot(slot);

        var result = await BookAsync();

        if (shouldBook)
        {
            Assert.IsType<BookingCreatedResult>(result);
        }
        else
        {
            var past = Assert.IsType<BookingSlotInPastResult>(result);
            Assert.Equal(slot.StartUtc, past.StartUtc);
            VerifyNothingWasWritten();
        }
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task An_unknown_slot_short_circuits_before_the_doctor_is_looked_up()
    {
        _slots
            .Setup(s => s.GetTrackedBySlotIdAsync(Ids.Slot, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Slot?)null);

        var result = await BookAsync();

        Assert.IsType<BookingSlotNotFoundResult>(result);
        _doctors.Verify(d => d.GetActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingWasWritten();
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task A_slot_whose_doctor_has_left_cannot_be_booked()
    {
        GivenSlot(SlotBuilder.ASlot().Build());
        _doctors
            .Setup(d => d.GetActiveAsync(Ids.Doctor, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DoctorCache?)null); // deactivated via staff.deactivated

        var result = await BookAsync();

        Assert.IsType<BookingDoctorNotFoundResult>(result);
        VerifyNothingWasWritten();
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task A_patient_already_booked_at_an_overlapping_time_gets_the_clash_explained()
    {
        var slot = SlotBuilder.ASlot().Build();
        GivenSlot(slot);
        var existing = AppointmentBuilder.AnAppointment()
            .StartingAt(slot.StartUtc.AddMinutes(-15))
            .Build();

        // The stub only answers for the exact window of this slot — a wrong window would get null.
        _appointments
            .Setup(a => a.FindPatientOverlapAsync(Ids.Patient, slot.StartUtc, slot.EndUtc, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await BookAsync();

        var clash = Assert.IsType<BookingPatientOverlapResult>(result);
        Assert.Equal(existing.AppointmentId, clash.ExistingAppointmentId);
        Assert.Equal(existing.StartUtc, clash.ExistingStartUtc);
        Assert.Equal(existing.EndUtc, clash.ExistingEndUtc);
        VerifyNothingWasWritten();
    }

    // ── Pricing integrity: the service code always comes from the trusted doctor cache ───

    [Theory]
    [InlineData("Cardiology", "GEN-CONSULT", "SPEC-CARDIOLOGY")]   // caller tries a cheaper code
    [InlineData("", "SPEC-CARDIOLOGY", "GEN-CONSULT")]             // no specialization -> general
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task The_billed_service_code_is_resolved_from_the_doctor_not_from_the_caller(
        string doctorSpecialization, string? callerServiceCode, string expectedServiceCode)
    {
        GivenSlot(SlotBuilder.ASlot().Build());
        _doctors
            .Setup(d => d.GetActiveAsync(Ids.Doctor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Doctors.Active(doctorSpecialization));

        OutboxMessage? published = null;
        _outbox
            .Setup(o => o.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxMessage, CancellationToken>((message, _) => published = message)
            .Returns(Task.CompletedTask);

        var result = await BookAsync(callerServiceCode);

        var created = Assert.IsType<BookingCreatedResult>(result);
        Assert.Equal(expectedServiceCode, created.Appointment.ServiceCode);

        // Billing (SCRUM-42) prices the invoice from this payload field, so check the wire format too.
        Assert.NotNull(published);
        using var payload = JsonDocument.Parse(published!.Payload);
        Assert.Equal(expectedServiceCode, payload.RootElement.GetProperty("serviceCode").GetString());
        Assert.Equal(Ids.Department, payload.RootElement.GetProperty("departmentId").GetGuid());
    }

    // ── SCRUM-35 concurrency: retry loop driven by a fake transaction that loses races ───

    [Theory]
    [InlineData(0, 1, typeof(BookingCreatedResult))]   // no contention
    [InlineData(2, 3, typeof(BookingCreatedResult))]   // won on the last permitted attempt
    [InlineData(3, 3, typeof(BookingContendedResult))] // MaxAttempts exhausted -> 409, never a 500
    [Trait(TestCategories.Technique, TestCategories.Parameterized)]
    public async Task Lost_concurrency_races_are_retried_up_to_three_attempts(
        int racesLost, int expectedAttempts, Type expectedResult)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var transaction = unitOfWork.SetupTransaction<BookingResult>(failFirst: racesLost);
        GivenSlot(SlotBuilder.ASlot().Build());

        var service = new AppointmentBookingService(
            _appointments.Object, _slots.Object, _doctors.Object, _outbox.Object,
            _history.Object, unitOfWork.Object, TestClock.Frozen());

        var result = await service.BookAsync(Ids.Slot, Ids.Patient, null, Actor, CorrelationId);

        Assert.IsType(expectedResult, result);
        Assert.Equal(expectedAttempts, transaction.Attempts);
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task A_retry_that_finds_the_slot_now_booked_reports_it_as_taken_by_the_race()
    {
        // Attempt 1 reads the slot Available, but the save loses on the xmin token.
        // Attempt 2 re-reads it and finds the winner already booked it.
        _slots
            .SetupSequence(s => s.GetTrackedBySlotIdAsync(Ids.Slot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SlotBuilder.ASlot().Build())
            .ReturnsAsync(SlotBuilder.ASlot().WithStatus(SlotStatus.Booked).Build());
        _unitOfWork
            .SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrentUpdateException());

        var result = await BookAsync();

        // Not "slot not available: Booked" — the caller is told they lost a race.
        Assert.IsType<BookingSlotTakenResult>(result);
        Assert.Equal(2, _transaction.Attempts);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task The_unique_index_backstop_is_final_and_is_not_retried()
    {
        GivenSlot(SlotBuilder.ASlot().Build());
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SlotAlreadyBookedException()); // ux_appointments_slot violated

        var result = await BookAsync();

        Assert.IsType<BookingSlotTakenResult>(result);
        Assert.Equal(1, _transaction.Attempts);
    }

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task An_unexpected_infrastructure_failure_is_not_swallowed_or_retried()
    {
        _slots
            .Setup(s => s.GetTrackedBySlotIdAsync(Ids.Slot, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("database unreachable"));

        await Assert.ThrowsAsync<TimeoutException>(() => BookAsync());
        Assert.Equal(1, _transaction.Attempts);
    }

    // ── Read side ────────────────────────────────────────────────────────────

    [Fact]
    [Trait(TestCategories.Technique, TestCategories.Mocking)]
    public async Task GetById_maps_a_stored_appointment_and_returns_null_for_an_unknown_id()
    {
        var stored = AppointmentBuilder.AnAppointment().Build();
        _appointments
            .Setup(a => a.GetByAppointmentIdAsync(Ids.Appointment, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var found = await CreateService().GetByIdAsync(Ids.Appointment);
        var missing = await CreateService().GetByIdAsync(Guid.NewGuid());

        Assert.NotNull(found);
        Assert.Equal(stored.AppointmentId, found!.AppointmentId);
        Assert.Equal(stored.Status, found.Status);
        Assert.Null(missing); // loose mock: an un-setup call returns the default (null)
    }
}
