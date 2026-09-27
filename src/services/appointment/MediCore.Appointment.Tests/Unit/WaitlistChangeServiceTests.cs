using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Services;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>
/// SCRUM-37: answering a waitlist entry — accepting an offer (AC4), declining it and leaving the
/// queue (AC3) — and the offer expiry that accepting enforces itself.
/// </summary>
public sealed class WaitlistChangeServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = Now.AddDays(2);
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherPatientId = Guid.Parse("2222222a-2222-2222-2222-222222222222");

    private static readonly AppointmentCaller Patient = new("PAT-000042", PatientId: PatientId);
    private static readonly AppointmentCaller Desk = new("desk@medicore.test");

    // ── Accept (AC4) ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Accepting_an_open_offer_books_the_slot_and_clears_the_entry()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1");

        var accepted = Assert.IsType<WaitlistAcceptedResult>(result);
        Assert.Equal(fixture.Entry.WaitlistEntryId, accepted.WaitlistEntryId);

        var appointment = Assert.Single(fixture.Appointments.Added);
        Assert.Equal(appointment.AppointmentId, accepted.Appointment.AppointmentId);
        Assert.Equal(PatientId, appointment.PatientId);
        Assert.Equal(fixture.Slot.SlotId, appointment.SlotId);
        Assert.Equal(Start, appointment.StartUtc);
        Assert.Equal(ServiceCodes.FollowUp, appointment.ServiceCode);
        Assert.Equal(AppointmentStatus.Booked, appointment.Status);
        Assert.Equal(SlotStatus.Booked, fixture.Slot.Status);

        Assert.Equal(WaitlistStatus.Accepted, fixture.Entry.Status);
        Assert.Equal(appointment.AppointmentId, fixture.Entry.AppointmentId);
        Assert.Equal(Now, fixture.Entry.ClosedAtUtc);

        // Exactly what a booking writes: the event and the first history entry.
        Assert.Equal("appointment.booked", Assert.Single(fixture.Outbox.Added).EventType);
        Assert.Equal(AppointmentHistoryAction.Booked, Assert.Single(fixture.History.Added).Action);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task The_front_desk_can_accept_on_the_patients_behalf()
    {
        // A staff token carries no patient claims; the entry's snapshot says who it is for.
        var fixture = new Fixture();

        var result = await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Desk, "corr-1");

        Assert.IsType<WaitlistAcceptedResult>(result);
        var appointment = Assert.Single(fixture.Appointments.Added);
        Assert.Equal(PatientId, appointment.PatientId);
        Assert.Equal("PAT-000042", appointment.PatientNumber);
        Assert.Equal("Nimal Perera", appointment.PatientName);
        Assert.Equal("desk@medicore.test", appointment.CreatedBy);
        Assert.Equal("desk@medicore.test", fixture.Entry.UpdatedBy);
    }

    [Fact]
    public async Task Accepting_locks_the_patient_then_the_queue_before_reading_the_entry()
    {
        var fixture = new Fixture();

        await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1");

        Assert.Equal(["locate entry", "lock patient", "lock queue", "read entry"], fixture.Log.Take(4));
    }

    [Fact]
    public async Task An_offer_cannot_be_accepted_at_the_instant_it_expires()
    {
        // AC3. Inclusive, and enforced here: the sweeper may not have closed it yet.
        var fixture = new Fixture();
        fixture.Entry.OfferExpiresAtUtc = Now;

        var result = await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1");

        Assert.Equal(Now, Assert.IsType<WaitlistOfferExpiredResult>(result).ExpiredAtUtc);
        fixture.AssertNothingWasWritten();
    }

    [Fact]
    public async Task An_offer_can_be_accepted_a_second_before_it_expires()
    {
        var fixture = new Fixture();
        fixture.Entry.OfferExpiresAtUtc = Now.AddSeconds(1);

        Assert.IsType<WaitlistAcceptedResult>(
            await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1"));
    }

    [Fact]
    public async Task A_lapsed_offer_the_sweeper_has_not_reached_is_still_refused()
    {
        var fixture = new Fixture();
        fixture.Entry.OfferExpiresAtUtc = Now.AddMinutes(-10);

        Assert.IsType<WaitlistOfferExpiredResult>(
            await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1"));
        Assert.Equal(WaitlistStatus.Offered, fixture.Entry.Status);
    }

    [Theory]
    [InlineData(WaitlistStatus.Waiting)]
    [InlineData(WaitlistStatus.Accepted)]
    [InlineData(WaitlistStatus.Declined)]
    [InlineData(WaitlistStatus.Expired)]
    [InlineData(WaitlistStatus.Withdrawn)]
    public async Task Only_an_open_offer_can_be_accepted(string status)
    {
        var fixture = new Fixture();
        fixture.Entry.Status = status;

        var result = await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1");

        var refused = Assert.IsType<WaitlistInvalidStateResult>(result);
        Assert.Equal(status, refused.CurrentStatus);
        Assert.Equal(WaitlistAction.Accept, refused.Action);
        fixture.AssertNothingWasWritten();
    }

    [Fact]
    public async Task Another_patients_entry_reads_as_not_found_and_nothing_is_locked()
    {
        var fixture = new Fixture();
        var stranger = new AppointmentCaller("PAT-000099", PatientId: OtherPatientId);

        var result = await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, stranger, "corr-1");

        Assert.IsType<WaitlistEntryNotFoundResult>(result);
        Assert.Equal(["locate entry"], fixture.Log);
    }

    [Fact]
    public async Task An_unknown_entry_is_not_found()
    {
        var fixture = new Fixture();

        Assert.IsType<WaitlistEntryNotFoundResult>(
            await fixture.Service.AcceptAsync(Guid.NewGuid(), Desk, "corr-1"));
    }

    [Fact]
    public async Task A_clash_with_another_appointment_is_refused_and_the_offer_stays_open()
    {
        var fixture = new Fixture();
        var other = new AppointmentEntity
        {
            PatientId = PatientId,
            StartUtc = Start.AddMinutes(-10),
            EndUtc = Start.AddMinutes(20),
            Status = AppointmentStatus.Booked
        };
        fixture.Appointments.Existing.Add(other);

        var result = await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1");

        Assert.Equal(other.AppointmentId, Assert.IsType<WaitlistPatientOverlapResult>(result).ExistingAppointmentId);
        Assert.Equal(WaitlistStatus.Offered, fixture.Entry.Status);
        Assert.Equal(SlotStatus.Offered, fixture.Slot.Status);
        fixture.AssertNothingWasWritten();
    }

    [Fact]
    public async Task An_offer_with_a_doctor_who_has_left_cannot_be_accepted()
    {
        var fixture = new Fixture();
        fixture.Doctors.Active.Clear();

        Assert.IsType<WaitlistOfferDoctorNotFoundResult>(
            await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1"));
        fixture.AssertNothingWasWritten();
    }

    [Theory]
    [InlineData(SlotStatus.Booked)]
    [InlineData(SlotStatus.Available)]
    [InlineData(SlotStatus.Blocked)]
    public async Task A_slot_no_longer_held_for_the_entry_is_not_taken(string status)
    {
        var fixture = new Fixture();
        fixture.Slot.Status = status;

        Assert.IsType<WaitlistOfferedSlotUnavailableResult>(
            await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1"));
        Assert.Equal(status, fixture.Slot.Status);
        fixture.AssertNothingWasWritten();
    }

    [Fact]
    public async Task A_deleted_slot_is_not_taken()
    {
        var fixture = new Fixture();
        fixture.Slots.Slots.Clear();

        Assert.IsType<WaitlistOfferedSlotUnavailableResult>(
            await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1"));
    }

    [Fact]
    public async Task Losing_to_the_one_appointment_per_slot_index_is_refused_and_rolled_back()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.Outcomes.Enqueue(new SlotAlreadyBookedException());

        var result = await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1");

        Assert.IsType<WaitlistOfferedSlotUnavailableResult>(result);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal(WaitlistStatus.Offered, fixture.Entry.Status);
        Assert.Equal(SlotStatus.Offered, fixture.Slot.Status);
    }

    [Fact]
    public async Task A_lost_race_rereads_everything_and_accepts_on_the_retry()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        var result = await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1");

        Assert.IsType<WaitlistAcceptedResult>(result);
        Assert.Equal(2, fixture.UnitOfWork.Transactions);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Single(fixture.Appointments.Added);
    }

    [Fact]
    public async Task Three_lost_races_ask_the_caller_to_try_again()
    {
        var fixture = new Fixture();
        for (var i = 0; i < 3; i++)
        {
            fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());
        }

        Assert.IsType<WaitlistChangeContendedResult>(
            await fixture.Service.AcceptAsync(fixture.Entry.WaitlistEntryId, Patient, "corr-1"));
        Assert.Equal(WaitlistStatus.Offered, fixture.Entry.Status);
    }

    // ── Decline (AC3) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Declining_passes_the_slot_on_and_closes_the_entry()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.DeclineAsync(fixture.Entry.WaitlistEntryId, Patient);

        var changed = Assert.IsType<WaitlistEntryChangedResult>(result);
        Assert.Equal(WaitlistStatus.Declined, changed.Entry.Status);
        Assert.Null(changed.Entry.PlaceInLine);

        var passedOn = Assert.Single(fixture.Offerer.PassedOn);
        Assert.Same(fixture.Entry, passedOn.Entry);
        Assert.Same(fixture.Slot, passedOn.Slot);
        Assert.Equal(WaitlistStatus.Declined, passedOn.Status);
        Assert.Null(passedOn.Reason);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);   // the next offer; the offerer saved the close
        Assert.Empty(fixture.Appointments.Added);
    }

    [Fact]
    public async Task Declining_takes_the_queue_lock_first()
    {
        var fixture = new Fixture();

        await fixture.Service.DeclineAsync(fixture.Entry.WaitlistEntryId, Patient);

        Assert.Equal(["locate entry", "lock queue", "read entry"], fixture.Log.Take(3));
    }

    [Fact]
    public async Task Only_an_open_offer_can_be_declined()
    {
        var fixture = new Fixture();
        fixture.Entry.Status = WaitlistStatus.Waiting;

        var result = await fixture.Service.DeclineAsync(fixture.Entry.WaitlistEntryId, Patient);

        Assert.Equal(WaitlistAction.Decline, Assert.IsType<WaitlistInvalidStateResult>(result).Action);
        Assert.Empty(fixture.Offerer.PassedOn);
    }

    [Fact]
    public async Task Another_patient_cannot_decline_an_offer()
    {
        var fixture = new Fixture();

        Assert.IsType<WaitlistEntryNotFoundResult>(await fixture.Service.DeclineAsync(
            fixture.Entry.WaitlistEntryId, new AppointmentCaller("x", PatientId: OtherPatientId)));
        Assert.Empty(fixture.Offerer.PassedOn);
    }

    // ── Withdraw ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Leaving_the_queue_while_waiting_closes_the_entry()
    {
        var fixture = new Fixture();
        fixture.Entry.Status = WaitlistStatus.Waiting;

        var result = await fixture.Service.WithdrawAsync(fixture.Entry.WaitlistEntryId, Patient, reason: null);

        Assert.Equal(WaitlistStatus.Withdrawn, Assert.IsType<WaitlistEntryChangedResult>(result).Entry.Status);
        Assert.Equal(Now, fixture.Entry.ClosedAtUtc);
        Assert.Equal("PAT-000042", fixture.Entry.UpdatedBy);
        Assert.Empty(fixture.Offerer.PassedOn);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Leaving_while_holding_an_offer_passes_the_slot_on()
    {
        var fixture = new Fixture();

        await fixture.Service.WithdrawAsync(fixture.Entry.WaitlistEntryId, Desk, "  Patient phoned.  ");

        var passedOn = Assert.Single(fixture.Offerer.PassedOn);
        Assert.Equal(WaitlistStatus.Withdrawn, passedOn.Status);
        Assert.Equal("Patient phoned.", passedOn.Reason);
        Assert.Same(fixture.Slot, passedOn.Slot);
    }

    [Fact]
    public async Task The_front_desks_reason_is_kept_and_a_blank_one_is_not()
    {
        var withReason = new Fixture();
        withReason.Entry.Status = WaitlistStatus.Waiting;
        var blank = new Fixture();
        blank.Entry.Status = WaitlistStatus.Waiting;

        await withReason.Service.WithdrawAsync(withReason.Entry.WaitlistEntryId, Desk, " Duplicate entry ");
        await blank.Service.WithdrawAsync(blank.Entry.WaitlistEntryId, Desk, "   ");

        Assert.Equal("Duplicate entry", withReason.Entry.ClosedReason);
        Assert.Null(blank.Entry.ClosedReason);
    }

    [Theory]
    [InlineData(WaitlistStatus.Accepted)]
    [InlineData(WaitlistStatus.Declined)]
    [InlineData(WaitlistStatus.Expired)]
    [InlineData(WaitlistStatus.Withdrawn)]
    public async Task A_closed_entry_cannot_be_withdrawn_again(string status)
    {
        var fixture = new Fixture();
        fixture.Entry.Status = status;

        var result = await fixture.Service.WithdrawAsync(fixture.Entry.WaitlistEntryId, Desk, reason: null);

        var refused = Assert.IsType<WaitlistInvalidStateResult>(result);
        Assert.Equal(status, refused.CurrentStatus);
        Assert.Equal(WaitlistAction.Withdraw, refused.Action);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class Fixture
    {
        private List<(object Entity, Dictionary<string, object?> Values)> _snapshot = [];

        public Fixture()
        {
            Slot = new Slot
            {
                DoctorId = DoctorId,
                StartUtc = Start,
                EndUtc = Start.AddMinutes(30),
                SlotDate = Application.Scheduling.ColomboTime.ToColomboDate(Start),
                DurationMinutes = 30,
                Status = SlotStatus.Offered
            };
            Entry = new WaitlistEntry
            {
                DoctorId = DoctorId,
                SlotDate = Slot.SlotDate,
                PatientId = PatientId,
                PatientNumber = "PAT-000042",
                PatientName = "Nimal Perera",
                ServiceCode = ServiceCodes.FollowUp,
                Position = 3,
                Status = WaitlistStatus.Offered,
                OfferedSlotId = Slot.SlotId,
                OfferedAtUtc = Now.AddMinutes(-10),
                OfferExpiresAtUtc = Now.AddMinutes(50)
            };

            Doctors.Active.Add(DoctorId);
            Waitlist = new FakeWaitlistRepository(Log, Entry);
            Appointments = new FakeAppointmentRepository(Log);
            Slots = new FakeSlotRepository(Slot);
            UnitOfWork = new FakeUnitOfWork(TakeSnapshot, Restore);

            Service = new WaitlistChangeService(
                Waitlist,
                Offerer,
                Appointments,
                Slots,
                Doctors,
                Outbox,
                History,
                UnitOfWork,
                new FixedTimeProvider(Now));
        }

        public List<string> Log { get; } = [];
        public Slot Slot { get; }
        public WaitlistEntry Entry { get; }
        public FakeWaitlistRepository Waitlist { get; }
        public FakeOfferer Offerer { get; } = new();
        public FakeAppointmentRepository Appointments { get; }
        public FakeSlotRepository Slots { get; }
        public FakeDoctorRepository Doctors { get; } = new();
        public FakeOutboxRepository Outbox { get; } = new();
        public FakeHistoryRepository History { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; }
        public WaitlistChangeService Service { get; }

        public void AssertNothingWasWritten()
        {
            Assert.Empty(Appointments.Added);
            Assert.Empty(Outbox.Added);
            Assert.Empty(History.Added);
            Assert.Equal(0, UnitOfWork.SaveCount);
        }

        private void TakeSnapshot() =>
            _snapshot = new object[] { Entry, Slot }
                .Select(entity => (entity, entity.GetType().GetProperties()
                    .Where(property => property.CanWrite)
                    .ToDictionary(property => property.Name, property => property.GetValue(entity))))
                .ToList();

        private void Restore()
        {
            foreach (var (entity, values) in _snapshot)
            {
                foreach (var (name, value) in values)
                {
                    entity.GetType().GetProperty(name)!.SetValue(entity, value);
                }
            }

            Appointments.Added.Clear();
            Outbox.Added.Clear();
            History.Added.Clear();
        }
    }

    private sealed class FakeWaitlistRepository(List<string> log, WaitlistEntry entry) : IWaitlistRepository
    {
        public Task<WaitlistEntry?> GetByEntryIdAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default)
        {
            log.Add("locate entry");
            return Task.FromResult(waitlistEntryId == entry.WaitlistEntryId ? entry : null);
        }

        public Task LockQueueAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken = default)
        {
            log.Add("lock queue");
            return Task.CompletedTask;
        }

        public Task<WaitlistEntry?> GetTrackedByEntryIdAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default)
        {
            log.Add("read entry");
            return Task.FromResult(waitlistEntryId == entry.WaitlistEntryId ? entry : null);
        }

        public Task<WaitlistListing?> GetListingAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WaitlistListing?>(new WaitlistListing(entry, "Dr. Silva", "Cardiology", null, null));

        public Task<int> GetNextPositionAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never add to a queue.");

        public Task<bool> HasActiveEntryAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never join.");

        public Task<int> CountActiveForPatientAsync(Guid patientId, DateOnly from, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never join.");

        public Task<bool> HasBookedWithDoctorOnAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Accepting checks the exact time, not the day.");

        public Task<IReadOnlyList<DayAvailability>> GetDayAvailabilityAsync(
            Guid doctorId, DateOnly from, DateOnly to, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never read a day.");

        public Task<IReadOnlyList<WaitlistEntry>> GetWaitingTrackedAsync(
            Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Passing on is the offerer's.");

        public Task<IReadOnlyList<WaitlistEntry>> GetActiveTrackedAsync(
            Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only the sweeper and schedule revision use this.");

        public Task<IReadOnlyList<WaitlistEntry>> GetTrackedOfferedForSlotsAsync(
            IReadOnlyCollection<Guid> slotIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only the sweeper and schedule revision use this.");

        public Task<IReadOnlyList<WaitlistEntry>> GetLapsedOffersAsync(
            DateTime nowUtc, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only the sweeper and schedule revision use this.");

        public Task<IReadOnlyList<Slot>> GetOrphanedOfferedSlotsAsync(
            int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only the sweeper and schedule revision use this.");

        public Task<bool> HasOpenOfferForSlotAsync(Guid slotId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only the sweeper and schedule revision use this.");

        public Task<IReadOnlyList<WaitlistQueue>> GetActiveQueuesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only the sweeper and schedule revision use this.");

        public Task AddAsync(WaitlistEntry entry, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never add to a queue.");

        public Task<IReadOnlyList<WaitlistListing>> ListForPatientAsync(
            Guid patientId, DateTime closedSince, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never list.");

        public Task<IReadOnlyList<WaitlistListing>> ListAsync(
            Guid? doctorId, DateOnly from, DateOnly to, IReadOnlyCollection<string>? statuses,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never list.");

        public Task<IReadOnlyList<WaitingPosition>> GetWaitingPositionsAsync(
            IReadOnlyCollection<Guid> doctorIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Closed entries have no place in line.");
    }

    /// <summary>Records each pass-on and does what the real offerer does to the entry and the slot.</summary>
    private sealed class FakeOfferer : IWaitlistOfferer
    {
        public List<(WaitlistEntry Entry, Slot? Slot, string Status, string? Reason)> PassedOn { get; } = [];

        public Task<WaitlistEntry?> PassOnAsync(
            WaitlistEntry entry, Slot? slot, string closingStatus, string? reason, DateTime nowUtc,
            string actor, CancellationToken cancellationToken = default)
        {
            PassedOn.Add((entry, slot, closingStatus, reason));
            entry.Status = closingStatus;
            entry.ClosedAtUtc = nowUtc;
            entry.ClosedReason = reason;
            if (slot is not null)
            {
                slot.Status = SlotStatus.Available;
            }

            return Task.FromResult<WaitlistEntry?>(null);
        }

        public Task<WaitlistEntry?> OfferAsync(
            Slot slot, DateTime nowUtc, string actor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers pass an offer on; they never make a fresh one.");
    }

    private sealed class FakeAppointmentRepository(List<string> log) : IAppointmentRepository
    {
        public List<AppointmentEntity> Existing { get; } = [];
        public List<AppointmentEntity> Added { get; } = [];

        public Task LockPatientAsync(Guid patientId, CancellationToken cancellationToken = default)
        {
            log.Add("lock patient");
            return Task.CompletedTask;
        }

        public Task<AppointmentEntity?> FindPatientOverlapAsync(
            Guid patientId, DateTime startUtc, DateTime endUtc, Guid? excludeAppointmentId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Existing.FirstOrDefault(appointment =>
                appointment.PatientId == patientId
                && appointment.Status == AppointmentStatus.Booked
                && appointment.StartUtc < endUtc
                && appointment.EndUtc > startUtc));

        public Task AddAsync(AppointmentEntity appointment, CancellationToken cancellationToken = default)
        {
            Added.Add(appointment);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AppointmentListing>> ListAsync(
            Guid? doctorId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never list appointments.");

        public Task<IReadOnlyList<AppointmentListing>> ListUpcomingForPatientAsync(
            Guid patientId, DateTime nowUtc, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never list appointments.");

        public Task<AppointmentEntity?> GetByAppointmentIdAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never read an appointment.");

        public Task<AppointmentEntity?> GetTrackedForUpdateAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never lock an appointment.");
    }

    private sealed class FakeSlotRepository(Slot slot) : ISlotRepository
    {
        public List<Slot> Slots { get; } = [slot];

        public Task<Slot?> GetTrackedBySlotIdAsync(Guid slotId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Slots.FirstOrDefault(candidate => candidate.SlotId == slotId));

        public Task<IReadOnlyList<Slot>> GetTrackedForDoctorBetweenAsync(
            Guid doctorId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers work from one slot.");

        public Task AddRangeAsync(IReadOnlyCollection<Slot> slots, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never create slots.");

        public void RemoveRange(IReadOnlyCollection<Slot> slots) =>
            throw new NotSupportedException("Answers never delete slots.");

        public Task<IReadOnlyList<Slot>> GetAvailableAsync(
            Guid doctorId, DateOnly from, DateOnly to, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers work from one slot.");

        public Task<IReadOnlyList<Slot>> GetFlaggedAsync(Guid? doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never read the attention list.");
    }

    private sealed class FakeDoctorRepository : IDoctorCacheRepository
    {
        public HashSet<Guid> Active { get; } = [];

        public Task<DoctorCache?> GetActiveAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active.Contains(doctorId)
                ? new DoctorCache { DoctorId = doctorId, IsActive = true }
                : null);

        public Task<DoctorCache?> GetTrackedByDoctorIdAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never update the doctor cache.");

        public Task<IReadOnlyList<DoctorCache>> ListActiveAsync(string? specialization, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never list doctors.");

        public Task<IReadOnlyList<string>> ListSpecializationsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never list specializations.");

        public Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never add doctors.");
    }

    private sealed class FakeOutboxRepository : IOutboxMessageRepository
    {
        public List<OutboxMessage> Added { get; } = [];

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Added.Add(message);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedBatchAsync(int batchSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only the dispatcher drains the outbox.");

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers commit through the unit of work.");
    }

    private sealed class FakeHistoryRepository : IAppointmentHistoryRepository
    {
        public List<AppointmentHistoryEntry> Added { get; } = [];

        public Task AddAsync(AppointmentHistoryEntry entry, CancellationToken cancellationToken = default)
        {
            Added.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AppointmentHistoryEntry>> ListForAppointmentAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Answers never read history.");
    }

    /// <summary>
    /// Runs each attempt as a transaction: a snapshot at the start, restored if the attempt throws.
    /// Each queued outcome is thrown by the next save in place of saving; null lets it through.
    /// </summary>
    private sealed class FakeUnitOfWork(Action onBegin, Action onRollback) : IUnitOfWork
    {
        public Queue<Exception?> Outcomes { get; } = new();
        public int SaveCount { get; private set; }
        public int Transactions { get; private set; }
        public int Commits { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (Outcomes.TryDequeue(out var outcome) && outcome is not null)
            {
                throw outcome;
            }

            SaveCount++;
            return Task.CompletedTask;
        }

        public async Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default)
        {
            Transactions++;
            onBegin();
            try
            {
                var result = await work(cancellationToken);
                Commits++;
                return result;
            }
            catch
            {
                onRollback();
                throw;
            }
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
