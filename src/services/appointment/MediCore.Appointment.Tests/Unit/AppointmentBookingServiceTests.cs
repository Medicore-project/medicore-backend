using System.Text.Json;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Services;
using MediCore.Contracts.Events.Appointment;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Tests.Unit;

public sealed class AppointmentBookingServiceTests
{
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherDoctorId = Guid.Parse("1111111a-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherPatientId = Guid.Parse("2222222a-2222-2222-2222-222222222222");
    private static readonly Guid SlotId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly DateTime Now = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SlotStart = new(2026, 9, 24, 3, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime SlotEnd = new(2026, 9, 24, 4, 0, 0, DateTimeKind.Utc);

    // ── AC1: booking creates the appointment and marks the slot taken ─────────

    [Fact]
    public async Task Booking_an_available_slot_takes_it_and_records_the_appointment()
    {
        var fixture = new Fixture(FreeSlot());

        var result = await fixture.Service.BookAsync(
            SlotId, PatientId, serviceCode: null, "desk@medicore.test", "corr-1");

        var created = Assert.IsType<BookingCreatedResult>(result);
        Assert.Equal(SlotStatus.Booked, fixture.Slots.Slot!.Status);
        Assert.Equal("desk@medicore.test", fixture.Slots.Slot.UpdatedBy);

        var appointment = Assert.Single(fixture.Appointments.Added);
        Assert.Equal(PatientId, appointment.PatientId);
        Assert.Equal(DoctorId, appointment.DoctorId);
        Assert.Equal(SlotId, appointment.SlotId);
        Assert.Equal(SlotStart, appointment.StartUtc);
        Assert.Equal(SlotEnd, appointment.EndUtc);
        Assert.Equal(30, appointment.DurationMinutes);
        Assert.Equal(AppointmentStatus.Booked, appointment.Status);
        Assert.Equal("desk@medicore.test", appointment.CreatedBy);
        Assert.Equal(appointment.AppointmentId, created.Appointment.AppointmentId);
    }

    [Fact]
    public async Task The_booking_records_who_it_is_for_so_staff_can_see_it()
    {
        var fixture = new Fixture(FreeSlot());

        var result = await fixture.Service.BookAsync(
            SlotId,
            PatientId,
            serviceCode: null,
            "desk",
            "corr-1",
            new BookingPatientDetails("PAT-000123", "Nimal Perera"));

        var appointment = Assert.Single(fixture.Appointments.Added);
        Assert.Equal("PAT-000123", appointment.PatientNumber);
        Assert.Equal("Nimal Perera", appointment.PatientName);

        var created = Assert.IsType<BookingCreatedResult>(result);
        Assert.Equal("PAT-000123", created.Appointment.PatientNumber);
        Assert.Equal("Nimal Perera", created.Appointment.PatientName);
    }

    [Fact]
    public async Task A_booking_with_nothing_to_copy_still_succeeds_without_a_name()
    {
        // A staff API call with a bare patientId: the booking is what matters, the label is not.
        var fixture = new Fixture(FreeSlot());

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.IsType<BookingCreatedResult>(result);
        var appointment = Assert.Single(fixture.Appointments.Added);
        Assert.Null(appointment.PatientNumber);
        Assert.Null(appointment.PatientName);
    }

    [Fact]
    public async Task The_slot_the_appointment_and_the_event_are_written_in_one_transaction()
    {
        // AC1 and AC4 together. A single SaveChanges is what makes them atomic: EF wraps one save
        // in one transaction, so a booking can never exist without its event row, and vice versa.
        var fixture = new Fixture(FreeSlot());

        await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Single(fixture.Appointments.Added);
        Assert.Single(fixture.Outbox.Added);
    }

    // ── AC4: the event carries the service code for billing ──────────────────

    [Fact]
    public async Task A_booking_announces_itself_with_the_service_code_billing_will_invoice()
    {
        var fixture = new Fixture(FreeSlot());

        await fixture.Service.BookAsync(
            SlotId, PatientId, ServiceCodes.SpecialistConsultation, "desk", "corr-xyz");

        var row = Assert.Single(fixture.Outbox.Added);
        Assert.Equal("appointment-events", row.Topic);
        Assert.Equal("appointment.booked", row.EventType);
        Assert.Equal("corr-xyz", row.CorrelationId);

        var published = JsonSerializer.Deserialize<AppointmentBookedEvent>(
            row.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(ServiceCodes.SpecialistConsultation, published.ServiceCode);
        Assert.Equal(PatientId, published.PatientId);
        Assert.Equal(DoctorId, published.DoctorId);
        Assert.Equal(SlotStart, published.SlotStart);
    }

    [Fact]
    public async Task A_booking_with_no_service_code_is_a_general_consultation()
    {
        var fixture = new Fixture(FreeSlot());

        await fixture.Service.BookAsync(SlotId, PatientId, serviceCode: null, "desk", "corr-1");

        Assert.Equal(
            ServiceCodes.GeneralConsultation,
            Assert.Single(fixture.Appointments.Added).ServiceCode);
    }

    // ── AC5: no publisher is reachable from here ─────────────────────────────

    [Fact]
    public void Booking_cannot_reach_kafka_even_if_it_wanted_to()
    {
        // The structural half of AC5. If a publisher is ever injected here, a broker outage stops
        // being survivable and this test is the one that should stop it happening.
        var dependencies = typeof(AppointmentBookingService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(IKafkaEventPublisher), dependencies);
    }

    // ── AC2: a slot in the past is refused ───────────────────────────────────

    [Fact]
    public async Task A_slot_whose_time_has_passed_cannot_be_booked()
    {
        var slot = FreeSlot();
        slot.StartUtc = Now.AddHours(-1);
        slot.EndUtc = Now.AddMinutes(-30);
        var fixture = new Fixture(slot);

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        var past = Assert.IsType<BookingSlotInPastResult>(result);
        Assert.Equal(slot.StartUtc, past.StartUtc);
        AssertNothingWasWritten(fixture);
    }

    [Fact]
    public async Task A_slot_starting_exactly_now_is_refused_too()
    {
        // The exact complement of the availability query's StartUtc >= nowUtc, so the listing and
        // the booking guard can never disagree about a single slot.
        var slot = FreeSlot();
        slot.StartUtc = Now;
        slot.EndUtc = Now.AddMinutes(30);
        var fixture = new Fixture(slot);

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.IsType<BookingSlotInPastResult>(result);
        AssertNothingWasWritten(fixture);
    }

    // ── AC3: a clash with the patient's own diary is refused ─────────────────

    [Fact]
    public async Task A_patient_cannot_be_in_two_places_at_once()
    {
        var existing = BookedAppointment(SlotStart.AddMinutes(15), SlotEnd.AddMinutes(15));
        var fixture = new Fixture(FreeSlot(), existing);

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        var overlap = Assert.IsType<BookingPatientOverlapResult>(result);
        Assert.Equal(existing.AppointmentId, overlap.ExistingAppointmentId);
        Assert.Equal(existing.StartUtc, overlap.ExistingStartUtc);
        Assert.Equal(existing.EndUtc, overlap.ExistingEndUtc);
        AssertNothingWasWritten(fixture);
    }

    [Fact]
    public async Task The_clash_counts_even_when_it_is_with_a_different_doctor()
    {
        // The objection is the patient's time, not the doctor's, so the overlap query is
        // deliberately not narrowed by doctor.
        var existing = BookedAppointment(SlotStart, SlotEnd);
        existing.DoctorId = OtherDoctorId;
        var fixture = new Fixture(FreeSlot(), existing);

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.IsType<BookingPatientOverlapResult>(result);
    }

    [Fact]
    public async Task Two_back_to_back_slots_do_not_clash()
    {
        // Slot generation produces contiguous slots where one ends exactly as the next begins, and
        // booking both is how a patient gets a longer consultation. Half-open intervals are what
        // make that work; a closed comparison would refuse it.
        var existing = BookedAppointment(SlotStart.AddMinutes(-30), SlotStart);
        var fixture = new Fixture(FreeSlot(), existing);

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.IsType<BookingCreatedResult>(result);
    }

    [Fact]
    public async Task A_cancelled_appointment_does_not_block_the_time_it_released()
    {
        var cancelled = BookedAppointment(SlotStart, SlotEnd);
        cancelled.Status = AppointmentStatus.Cancelled;
        var fixture = new Fixture(FreeSlot(), cancelled);

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.IsType<BookingCreatedResult>(result);
    }

    [Fact]
    public async Task Another_patients_appointment_at_the_same_time_is_not_a_clash()
    {
        var someoneElse = BookedAppointment(SlotStart, SlotEnd);
        someoneElse.PatientId = OtherPatientId;
        var fixture = new Fixture(FreeSlot(), someoneElse);

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.IsType<BookingCreatedResult>(result);
    }

    // ── The remaining guards ─────────────────────────────────────────────────

    [Fact]
    public async Task Booking_a_slot_that_does_not_exist_is_a_not_found()
    {
        var fixture = new Fixture(slot: null);

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.IsType<BookingSlotNotFoundResult>(result);
        AssertNothingWasWritten(fixture);
    }

    [Fact]
    public async Task A_doctor_who_has_left_the_clinic_keeps_their_slots_but_cannot_be_booked()
    {
        // SCRUM-33 leaves a deactivated doctor's slot rows in place on purpose, so this check is
        // the only thing standing between a patient and an appointment with someone who has gone.
        var fixture = new Fixture(FreeSlot()) { Doctors = { IsBookable = false } };

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.IsType<BookingDoctorNotFoundResult>(result);
        AssertNothingWasWritten(fixture);
    }

    [Theory]
    [InlineData(SlotStatus.Booked)]
    [InlineData(SlotStatus.Blocked)]
    [InlineData(SlotStatus.Flagged)]
    public async Task A_slot_that_is_not_free_is_refused_and_the_reason_is_named(string status)
    {
        var slot = FreeSlot();
        slot.Status = status;
        var fixture = new Fixture(slot);

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        var unavailable = Assert.IsType<BookingSlotNotAvailableResult>(result);
        Assert.Equal(status, unavailable.CurrentStatus);
        AssertNothingWasWritten(fixture, expectedSlotStatus: status);
    }

    [Fact]
    public async Task Losing_the_race_to_the_unique_index_is_reported_rather_than_retried()
    {
        // Both racers passed the status check; the database decided. Nothing committed, so the
        // service must report and stop — retrying on the same cleared context would be a bug.
        var fixture = new Fixture(FreeSlot());
        fixture.UnitOfWork.Throw = new SlotAlreadyBookedException();

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.IsType<BookingSlotTakenResult>(result);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    // ── SCRUM-35: transaction, patient lock and retry ────────────────────────

    [Fact]
    public async Task Losing_to_the_index_rolls_the_attempt_back_and_leaves_nothing()
    {
        var fixture = new Fixture(FreeSlot());
        fixture.UnitOfWork.Throw = new SlotAlreadyBookedException();

        await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.Equal(1, fixture.UnitOfWork.Transactions);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Empty(fixture.Appointments.Added);
        Assert.Empty(fixture.Outbox.Added);
    }

    [Fact]
    public async Task Losing_the_slot_token_race_rereads_the_slot_and_reports_it_taken()
    {
        // Both racers read Available; the other one committed first, so our save matched no row.
        // The retry reads the slot as the winner left it.
        var fixture = new Fixture(FreeSlot());
        fixture.Slots.Rereads.Enqueue(SlotIn(SlotStatus.Booked));
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        // In the words of the race, not "it is Booked": this caller did not book it.
        Assert.IsType<BookingSlotTakenResult>(result);
        Assert.Equal(2, fixture.UnitOfWork.Transactions);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.Appointments.Added);
        Assert.Empty(fixture.Outbox.Added);
    }

    [Fact]
    public async Task A_retry_that_finds_the_slot_blocked_names_the_block()
    {
        // Not every lost race is a booking: an admin can block the slot in between. Rewording
        // that as "someone booked it" would be false.
        var fixture = new Fixture(FreeSlot());
        fixture.Slots.Rereads.Enqueue(SlotIn(SlotStatus.Blocked));
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        var refused = Assert.IsType<BookingSlotNotAvailableResult>(result);
        Assert.Equal(SlotStatus.Blocked, refused.CurrentStatus);
    }

    [Fact]
    public async Task A_retry_books_the_slot_when_it_is_still_free()
    {
        // The row changed but the slot is still Available: retrying is what lets this caller win
        // instead of being turned away by a race that did not actually take the slot.
        var fixture = new Fixture(FreeSlot());
        fixture.Slots.Rereads.Enqueue(FreeSlot());
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        var created = Assert.IsType<BookingCreatedResult>(result);
        // Exactly one appointment and one event, from the attempt that committed.
        var appointment = Assert.Single(fixture.Appointments.Added);
        Assert.Equal(created.Appointment.AppointmentId, appointment.AppointmentId);
        Assert.Single(fixture.Outbox.Added);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(2, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Three_lost_races_in_a_row_end_in_a_conflict_not_an_error()
    {
        var fixture = new Fixture(FreeSlot());
        fixture.Slots.Rereads.Enqueue(FreeSlot());
        fixture.Slots.Rereads.Enqueue(FreeSlot());
        fixture.UnitOfWork.Throw = new ConcurrentUpdateException();

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.IsType<BookingContendedResult>(result);
        Assert.Equal(3, fixture.UnitOfWork.Transactions);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Empty(fixture.Appointments.Added);
        Assert.Empty(fixture.Outbox.Added);
    }

    [Fact]
    public async Task The_patient_is_locked_first_inside_the_transaction_before_anything_is_read()
    {
        // The lock only serializes the overlap check if it is held before the check reads and
        // until the booking commits.
        var fixture = new Fixture(FreeSlot());

        await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.Equal(
            ["begin", $"lock {PatientId}", "read slot", "check overlap", "save", "commit"],
            fixture.Log);
    }

    [Fact]
    public async Task Every_attempt_takes_the_lock_again_in_its_own_transaction()
    {
        // The lock is transaction-scoped, so the rollback released it; a retry that skipped it
        // would check for overlaps unprotected.
        var fixture = new Fixture(FreeSlot());
        fixture.Slots.Rereads.Enqueue(FreeSlot());
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.Equal(
            [
                "begin", $"lock {PatientId}", "read slot", "check overlap", "save", "rollback",
                "begin", $"lock {PatientId}", "read slot", "check overlap", "save", "commit"
            ],
            fixture.Log);
    }

    [Fact]
    public async Task The_status_check_comes_before_the_time_check()
    {
        // A blocked slot in the past is reported as blocked: the more fundamental objection, and
        // the more useful thing to tell whoever is looking at it.
        var slot = FreeSlot();
        slot.Status = SlotStatus.Blocked;
        slot.StartUtc = Now.AddHours(-1);
        slot.EndUtc = Now.AddMinutes(-30);
        var fixture = new Fixture(slot);

        var result = await fixture.Service.BookAsync(SlotId, PatientId, null, "desk", "corr-1");

        Assert.IsType<BookingSlotNotAvailableResult>(result);
        AssertNothingWasWritten(fixture, expectedSlotStatus: SlotStatus.Blocked);
    }

    // ── Reading one back ─────────────────────────────────────────────────────

    [Fact]
    public async Task An_unknown_appointment_id_reads_back_as_null()
    {
        var fixture = new Fixture(FreeSlot());

        Assert.Null(await fixture.Service.GetByIdAsync(Guid.NewGuid()));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A refusal must leave the world exactly as it found it: no appointment, no event, no save,
    /// and the slot still carrying the status it arrived with.
    /// </summary>
    private static void AssertNothingWasWritten(Fixture fixture, string expectedSlotStatus = SlotStatus.Available)
    {
        Assert.Empty(fixture.Appointments.Added);
        Assert.Empty(fixture.Outbox.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);

        if (fixture.Slots.Slot is not null)
        {
            Assert.Equal(expectedSlotStatus, fixture.Slots.Slot.Status);
            Assert.Null(fixture.Slots.Slot.UpdatedBy);
        }
    }

    private static Slot FreeSlot() => new()
    {
        SlotId = SlotId,
        DoctorId = DoctorId,
        StartUtc = SlotStart,
        EndUtc = SlotEnd,
        SlotDate = new DateOnly(2026, 9, 24),
        DurationMinutes = 30,
        Status = SlotStatus.Available
    };

    private static Slot SlotIn(string status)
    {
        var slot = FreeSlot();
        slot.Status = status;
        return slot;
    }

    private static AppointmentEntity BookedAppointment(DateTime startUtc, DateTime endUtc) => new()
    {
        SlotId = Guid.NewGuid(),
        PatientId = PatientId,
        DoctorId = DoctorId,
        StartUtc = startUtc,
        EndUtc = endUtc,
        SlotDate = new DateOnly(2026, 9, 24),
        DurationMinutes = 30,
        Status = AppointmentStatus.Booked
    };

    private sealed class Fixture
    {
        public Fixture(Slot? slot, params AppointmentEntity[] existing)
        {
            Slots = new FakeSlotRepository(slot);
            Appointments = new FakeAppointmentRepository(existing);
            Doctors = new FakeDoctorCacheRepository();
            Outbox = new FakeOutboxMessageRepository();
            // A rolled-back attempt leaves nothing: what it staged disappears, as it would from
            // the database.
            UnitOfWork = new FakeUnitOfWork(Log, onRollback: () =>
            {
                Appointments.Added.Clear();
                Outbox.Added.Clear();
            });
            Slots.Log = Log;
            Appointments.Log = Log;

            Service = new AppointmentBookingService(
                Appointments, Slots, Doctors, Outbox, UnitOfWork, new FixedTimeProvider(Now));
        }

        /// <summary>Every step of every attempt, in order, across the fakes.</summary>
        public List<string> Log { get; } = [];

        public FakeSlotRepository Slots { get; }

        public FakeAppointmentRepository Appointments { get; }

        public FakeDoctorCacheRepository Doctors { get; }

        public FakeOutboxMessageRepository Outbox { get; }

        public FakeUnitOfWork UnitOfWork { get; }

        public AppointmentBookingService Service { get; }
    }

    private sealed class FakeSlotRepository : ISlotRepository
    {
        public FakeSlotRepository(Slot? slot)
        {
            Slot = slot;
        }

        /// <summary>What the first read returns.</summary>
        public Slot? Slot { get; }

        /// <summary>
        /// What each later read returns, in turn: the slot as another writer left it. The real
        /// unit of work clears the tracker on a lost race, so a retry reads a fresh entity rather
        /// than the one the failed attempt already changed.
        /// </summary>
        public Queue<Slot> Rereads { get; } = new();

        public List<string>? Log { get; set; }

        private int _reads;

        public Task<Slot?> GetTrackedBySlotIdAsync(Guid slotId, CancellationToken cancellationToken = default)
        {
            Log?.Add("read slot");
            var slot = _reads++ > 0 && Rereads.TryDequeue(out var reread) ? reread : Slot;
            return Task.FromResult(slot?.SlotId == slotId ? slot : null);
        }

        public Task<IReadOnlyList<Slot>> GetTrackedForDoctorBetweenAsync(
            Guid doctorId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Booking never reconciles a schedule.");

        public Task AddRangeAsync(IReadOnlyCollection<Slot> slots, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Booking never creates slots.");

        public void RemoveRange(IReadOnlyCollection<Slot> slots) =>
            throw new NotSupportedException("Booking never deletes slots.");

        public Task<IReadOnlyList<Slot>> GetAvailableAsync(
            Guid doctorId, DateOnly from, DateOnly to, DateTime nowUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Booking works from one slot id, not a listing.");

        public Task<IReadOnlyList<Slot>> GetFlaggedAsync(
            Guid? doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Booking never reads the attention list.");
    }

    private sealed class FakeAppointmentRepository : IAppointmentRepository
    {
        private readonly List<AppointmentEntity> _existing;

        public FakeAppointmentRepository(IEnumerable<AppointmentEntity> existing)
        {
            _existing = [.. existing];
        }

        public List<AppointmentEntity> Added { get; } = [];

        public List<string>? Log { get; set; }

        public Task AddAsync(AppointmentEntity appointment, CancellationToken cancellationToken = default)
        {
            Added.Add(appointment);
            return Task.CompletedTask;
        }

        public Task LockPatientAsync(Guid patientId, CancellationToken cancellationToken = default)
        {
            Log?.Add($"lock {patientId}");
            return Task.CompletedTask;
        }

        // The half-open comparison the real repository makes, so the interval rules are exercised
        // here rather than assumed.
        public Task<AppointmentEntity?> FindPatientOverlapAsync(
            Guid patientId, DateTime startUtc, DateTime endUtc,
            CancellationToken cancellationToken = default)
        {
            Log?.Add("check overlap");
            return Task.FromResult(_existing
                .Where(appointment =>
                    appointment.PatientId == patientId
                    && appointment.Status == AppointmentStatus.Booked
                    && appointment.StartUtc < endUtc
                    && appointment.EndUtc > startUtc)
                .OrderBy(appointment => appointment.StartUtc)
                .FirstOrDefault());
        }

        public Task<AppointmentEntity?> GetByAppointmentIdAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_existing
                .Concat(Added)
                .FirstOrDefault(appointment => appointment.AppointmentId == appointmentId));

        public Task<IReadOnlyList<AppointmentListing>> ListAsync(
            Guid? doctorId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Booking never lists appointments.");

        public Task<IReadOnlyList<AppointmentListing>> ListUpcomingForPatientAsync(
            Guid patientId, DateTime nowUtc, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Booking never lists appointments.");
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
            throw new NotSupportedException("Booking never writes the cache.");

        public Task<IReadOnlyList<DoctorCache>> ListActiveAsync(
            string? specialization, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Booking works from one doctor, not a listing.");

        public Task<IReadOnlyList<string>> ListSpecializationsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only the public booking page lists specializations.");

        public Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Booking never writes the cache.");
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
            throw new NotSupportedException("Booking commits through the unit of work.");
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        private readonly List<string> _log;
        private readonly Action _onRollback;

        public FakeUnitOfWork(List<string> log, Action onRollback)
        {
            _log = log;
            _onRollback = onRollback;
        }

        public int SaveCount { get; private set; }

        public int Transactions { get; private set; }

        public int Commits { get; private set; }

        /// <summary>Thrown by every save. <see cref="Outcomes"/> takes precedence while it lasts.</summary>
        public Exception? Throw { get; set; }

        /// <summary>What each save does in turn; a null entry means that save succeeds.</summary>
        public Queue<Exception?> Outcomes { get; } = new();

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
