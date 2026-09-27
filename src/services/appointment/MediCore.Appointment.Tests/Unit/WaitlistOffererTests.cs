using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using Microsoft.Extensions.Options;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>
/// SCRUM-37 AC2 and AC3: who a released slot is offered to, for how long, and where it goes when
/// the offer is let go. Part of the [QA] unit test "waitlist position ordering and offer expiry".
/// </summary>
public sealed class WaitlistOffererTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Start = Now.AddDays(2);
    private static readonly DateOnly Day = ColomboTime.ToColomboDate(Start);

    // ── Offering a released slot (AC2) ────────────────────────────────────────

    [Fact]
    public async Task The_lowest_waiting_position_gets_the_offer()
    {
        var fixture = new Fixture();
        fixture.Add(1, WaitlistStatus.Expired);
        var second = fixture.Add(5, WaitlistStatus.Waiting);
        var first = fixture.Add(2, WaitlistStatus.Waiting);

        var offered = await fixture.OfferAsync();

        Assert.Same(first, offered);
        Assert.Equal(WaitlistStatus.Offered, first.Status);
        Assert.Equal(fixture.Slot.SlotId, first.OfferedSlotId);
        Assert.Equal(Now, first.OfferedAtUtc);
        Assert.Equal("desk", first.UpdatedBy);
        Assert.Equal(WaitlistStatus.Waiting, second.Status);
        Assert.Equal(SlotStatus.Offered, fixture.Slot.Status);
        Assert.Equal("desk", fixture.Slot.UpdatedBy);
    }

    [Fact]
    public async Task The_offer_is_held_for_the_configured_window()
    {
        var fixture = new Fixture(offerWindowMinutes: 60);
        var entry = fixture.Add(1, WaitlistStatus.Waiting);

        await fixture.OfferAsync();

        Assert.Equal(Now.AddMinutes(60), entry.OfferExpiresAtUtc);
    }

    [Fact]
    public async Task The_offer_never_outlives_the_slots_start()
    {
        var fixture = new Fixture(offerWindowMinutes: 60);
        fixture.Slot.StartUtc = Now.AddMinutes(40);
        fixture.Slot.EndUtc = Now.AddMinutes(70);
        var entry = fixture.Add(1, WaitlistStatus.Waiting);

        await fixture.OfferAsync();

        Assert.Equal(fixture.Slot.StartUtc, entry.OfferExpiresAtUtc);
    }

    [Fact]
    public async Task With_nobody_waiting_the_slot_stays_available()
    {
        var fixture = new Fixture();
        fixture.Add(1, WaitlistStatus.Accepted);

        Assert.Null(await fixture.OfferAsync());
        Assert.Equal(SlotStatus.Available, fixture.Slot.Status);
    }

    [Fact]
    public async Task The_queue_is_locked_before_its_candidates_are_read()
    {
        var fixture = new Fixture();
        fixture.Add(1, WaitlistStatus.Waiting);

        await fixture.OfferAsync();

        Assert.Equal(["lock queue", "read waiting"], fixture.Waitlist.Log.Take(2));
    }

    [Fact]
    public async Task Offering_stages_the_change_and_leaves_the_save_to_the_caller()
    {
        var fixture = new Fixture();
        fixture.Add(1, WaitlistStatus.Waiting);

        await fixture.OfferAsync();

        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_patient_already_booked_with_the_doctor_that_day_is_withdrawn_and_passed_over()
    {
        // Booked some other way while waiting — or it was their own cancellation that freed this.
        var fixture = new Fixture();
        var booked = fixture.Add(1, WaitlistStatus.Waiting);
        var next = fixture.Add(2, WaitlistStatus.Waiting);
        fixture.Waitlist.Booked.Add(booked.PatientId);

        var offered = await fixture.OfferAsync();

        Assert.Same(next, offered);
        Assert.Equal(WaitlistStatus.Withdrawn, booked.Status);
        Assert.Equal(WaitlistOfferer.AlreadyBookedReason, booked.ClosedReason);
        Assert.Equal(Now, booked.ClosedAtUtc);
    }

    [Fact]
    public async Task A_patient_busy_at_that_time_is_passed_over_but_keeps_their_place()
    {
        var fixture = new Fixture();
        var busy = fixture.Add(1, WaitlistStatus.Waiting);
        var next = fixture.Add(2, WaitlistStatus.Waiting);
        fixture.Appointments.Add(new AppointmentEntity
        {
            PatientId = busy.PatientId,
            StartUtc = Start.AddMinutes(-15),
            EndUtc = Start.AddMinutes(15),
            Status = AppointmentStatus.Booked
        });

        var offered = await fixture.OfferAsync();

        Assert.Same(next, offered);
        Assert.Equal(WaitlistStatus.Waiting, busy.Status);
        Assert.Null(busy.ClosedAtUtc);
    }

    [Fact]
    public async Task When_nobody_waiting_can_take_it_the_slot_stays_available()
    {
        var fixture = new Fixture();
        var booked = fixture.Add(1, WaitlistStatus.Waiting);
        fixture.Waitlist.Booked.Add(booked.PatientId);

        Assert.Null(await fixture.OfferAsync());
        Assert.Equal(SlotStatus.Available, fixture.Slot.Status);
    }

    [Fact]
    public async Task A_slot_starting_inside_the_minimum_lead_is_not_offered()
    {
        // Nobody could answer and arrive in time; the public list may find someone nearby.
        var fixture = new Fixture(minimumLeadMinutes: 15);
        fixture.Slot.StartUtc = Now.AddMinutes(14);
        fixture.Add(1, WaitlistStatus.Waiting);

        Assert.Null(await fixture.OfferAsync());
        Assert.Equal(SlotStatus.Available, fixture.Slot.Status);
        Assert.Empty(fixture.Waitlist.Log);
    }

    [Fact]
    public async Task A_slot_starting_exactly_at_the_minimum_lead_is_offered()
    {
        var fixture = new Fixture(minimumLeadMinutes: 15);
        fixture.Slot.StartUtc = Now.AddMinutes(15);
        fixture.Slot.EndUtc = Now.AddMinutes(45);
        fixture.Add(1, WaitlistStatus.Waiting);

        Assert.NotNull(await fixture.OfferAsync());
    }

    [Fact]
    public async Task A_slot_with_a_doctor_who_has_left_is_not_offered()
    {
        var fixture = new Fixture();
        fixture.Doctors.Active.Clear();
        fixture.Add(1, WaitlistStatus.Waiting);

        Assert.Null(await fixture.OfferAsync());
        Assert.Empty(fixture.Waitlist.Log);
    }

    [Theory]
    [InlineData(SlotStatus.Booked)]
    [InlineData(SlotStatus.Blocked)]
    [InlineData(SlotStatus.Offered)]
    [InlineData(SlotStatus.Flagged)]
    public async Task Only_an_available_slot_is_offered(string status)
    {
        var fixture = new Fixture();
        fixture.Slot.Status = status;
        fixture.Add(1, WaitlistStatus.Waiting);

        Assert.Null(await fixture.OfferAsync());
        Assert.Equal(status, fixture.Slot.Status);
        Assert.Empty(fixture.Waitlist.Log);
    }

    // ── Passing an offer on (AC3) ─────────────────────────────────────────────

    [Fact]
    public async Task A_declined_offer_passes_to_the_next_in_line()
    {
        var fixture = new Fixture();
        var holder = fixture.Holding(1);
        var next = fixture.Add(3, WaitlistStatus.Waiting);

        var offered = await fixture.PassOnAsync(holder, WaitlistStatus.Declined, reason: null);

        Assert.Same(next, offered);
        Assert.Equal(WaitlistStatus.Declined, holder.Status);
        Assert.Equal(Now, holder.ClosedAtUtc);
        Assert.Equal(WaitlistStatus.Offered, next.Status);
        Assert.Equal(fixture.Slot.SlotId, next.OfferedSlotId);
        Assert.Equal(SlotStatus.Offered, fixture.Slot.Status);
    }

    [Fact]
    public async Task The_old_offer_is_saved_closed_before_the_next_is_made()
    {
        // Both entries carry the slot id; the open-offer index must never see two at once.
        var fixture = new Fixture();
        var holder = fixture.Holding(1);
        var next = fixture.Add(3, WaitlistStatus.Waiting);

        await fixture.PassOnAsync(holder, WaitlistStatus.Expired, reason: null);

        var saved = Assert.Single(fixture.UnitOfWork.Saves);
        Assert.Equal(WaitlistStatus.Expired, saved[holder]);
        Assert.Equal(WaitlistStatus.Waiting, saved[next]);
        Assert.Equal(SlotStatus.Available, fixture.UnitOfWork.SlotStatusAtSave.Single());
    }

    [Fact]
    public async Task An_expired_offer_with_nobody_behind_it_frees_the_slot()
    {
        var fixture = new Fixture();
        var holder = fixture.Holding(1);

        Assert.Null(await fixture.PassOnAsync(holder, WaitlistStatus.Expired, reason: null));
        Assert.Equal(WaitlistStatus.Expired, holder.Status);
        Assert.Equal(SlotStatus.Available, fixture.Slot.Status);
    }

    [Fact]
    public async Task The_entry_letting_go_is_not_offered_the_slot_again()
    {
        var fixture = new Fixture();
        var holder = fixture.Holding(1);

        await fixture.PassOnAsync(holder, WaitlistStatus.Declined, reason: null);

        Assert.Equal(WaitlistStatus.Declined, holder.Status);
    }

    [Fact]
    public async Task A_withdrawal_keeps_its_reason()
    {
        var fixture = new Fixture();
        var holder = fixture.Holding(1);

        await fixture.PassOnAsync(holder, WaitlistStatus.Withdrawn, reason: "Patient phoned to cancel.");

        Assert.Equal("Patient phoned to cancel.", holder.ClosedReason);
    }

    [Theory]
    [InlineData(SlotStatus.Booked)]
    [InlineData(SlotStatus.Available)]
    [InlineData(SlotStatus.Blocked)]
    public async Task A_slot_the_entry_no_longer_holds_is_left_alone(string status)
    {
        var fixture = new Fixture();
        var holder = fixture.Holding(1);
        fixture.Slot.Status = status;
        fixture.Add(3, WaitlistStatus.Waiting);

        Assert.Null(await fixture.PassOnAsync(holder, WaitlistStatus.Expired, reason: null));
        Assert.Equal(status, fixture.Slot.Status);
        Assert.Equal(WaitlistStatus.Expired, holder.Status);
    }

    [Fact]
    public async Task A_slot_held_for_a_different_entry_is_left_alone()
    {
        var fixture = new Fixture();
        var holder = fixture.Holding(1);
        holder.OfferedSlotId = Guid.NewGuid();

        Assert.Null(await fixture.PassOnAsync(holder, WaitlistStatus.Expired, reason: null));
        Assert.Equal(SlotStatus.Offered, fixture.Slot.Status);
    }

    [Fact]
    public async Task An_offer_whose_slot_was_deleted_is_just_closed()
    {
        var fixture = new Fixture();
        var holder = fixture.Holding(1);

        var offered = await fixture.Offerer.PassOnAsync(
            holder, slot: null, WaitlistStatus.Expired, reason: null, Now, "system");

        Assert.Null(offered);
        Assert.Equal(WaitlistStatus.Expired, holder.Status);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class Fixture
    {
        public FakeWaitlistRepository Waitlist { get; } = new();
        public List<AppointmentEntity> Appointments { get; } = [];
        public FakeDoctorRepository Doctors { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; }
        public WaitlistOfferer Offerer { get; }

        public Slot Slot { get; } = new()
        {
            DoctorId = DoctorId,
            StartUtc = Start,
            EndUtc = Start.AddMinutes(30),
            SlotDate = Day,
            DurationMinutes = 30,
            Status = SlotStatus.Available
        };

        public Fixture(int offerWindowMinutes = 60, int minimumLeadMinutes = 15)
        {
            Doctors.Active.Add(DoctorId);
            UnitOfWork = new FakeUnitOfWork(Waitlist.Entries, Slot);
            Offerer = new WaitlistOfferer(
                Waitlist,
                new FakeAppointmentRepository(Appointments),
                Doctors,
                UnitOfWork,
                Options.Create(new WaitlistOptions
                {
                    OfferWindowMinutes = offerWindowMinutes,
                    MinimumLeadMinutes = minimumLeadMinutes
                }));
        }

        public WaitlistEntry Add(int position, string status)
        {
            var entry = new WaitlistEntry
            {
                DoctorId = DoctorId,
                SlotDate = Day,
                PatientId = Guid.NewGuid(),
                Position = position,
                Status = status
            };
            Waitlist.Entries.Add(entry);
            return entry;
        }

        /// <summary>An entry already holding <see cref="Slot"/>.</summary>
        public WaitlistEntry Holding(int position)
        {
            var entry = Add(position, WaitlistStatus.Offered);
            entry.OfferedSlotId = Slot.SlotId;
            entry.OfferedAtUtc = Now.AddMinutes(-60);
            entry.OfferExpiresAtUtc = Now;
            Slot.Status = SlotStatus.Offered;
            return entry;
        }

        public Task<WaitlistEntry?> OfferAsync() => Offerer.OfferAsync(Slot, Now, "desk");

        public Task<WaitlistEntry?> PassOnAsync(WaitlistEntry entry, string status, string? reason) =>
            Offerer.PassOnAsync(entry, Slot, status, reason, Now, "system");
    }

    private sealed class FakeWaitlistRepository : IWaitlistRepository
    {
        public List<WaitlistEntry> Entries { get; } = [];
        public HashSet<Guid> Booked { get; } = [];
        public List<string> Log { get; } = [];

        public Task LockQueueAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken = default)
        {
            Log.Add("lock queue");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WaitlistEntry>> GetWaitingTrackedAsync(
            Guid doctorId, DateOnly date, CancellationToken cancellationToken = default)
        {
            Log.Add("read waiting");
            IReadOnlyList<WaitlistEntry> waiting = Entries
                .Where(entry => entry.DoctorId == doctorId && entry.SlotDate == date
                    && entry.Status == WaitlistStatus.Waiting)
                .OrderBy(entry => entry.Position)
                .ToList();
            return Task.FromResult(waiting);
        }

        public Task<bool> HasBookedWithDoctorOnAsync(
            Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult(Booked.Contains(patientId));

        public Task<int> GetNextPositionAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never adds to a queue.");

        public Task<bool> HasActiveEntryAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never joins.");

        public Task<int> CountActiveForPatientAsync(Guid patientId, DateOnly from, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never joins.");

        public Task<IReadOnlyList<DayAvailability>> GetDayAvailabilityAsync(
            Guid doctorId, DateOnly from, DateOnly to, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering works from one slot, not a day.");

        public Task AddAsync(WaitlistEntry entry, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never adds to a queue.");

        public Task<WaitlistListing?> GetListingAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never reads listings.");

        public Task<IReadOnlyList<WaitlistListing>> ListForPatientAsync(
            Guid patientId, DateTime closedSince, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never reads listings.");

        public Task<IReadOnlyList<WaitlistListing>> ListAsync(
            Guid? doctorId, DateOnly from, DateOnly to, IReadOnlyCollection<string>? statuses,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never reads listings.");

        public Task<IReadOnlyList<WaitingPosition>> GetWaitingPositionsAsync(
            IReadOnlyCollection<Guid> doctorIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never works out places.");
    }

    /// <summary>Overlap only, with the real repository's half-open predicate.</summary>
    private sealed class FakeAppointmentRepository(List<AppointmentEntity> appointments) : IAppointmentRepository
    {
        public Task<AppointmentEntity?> FindPatientOverlapAsync(
            Guid patientId, DateTime startUtc, DateTime endUtc, Guid? excludeAppointmentId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(appointments.FirstOrDefault(appointment =>
                appointment.PatientId == patientId
                && appointment.Status == AppointmentStatus.Booked
                && appointment.StartUtc < endUtc
                && appointment.EndUtc > startUtc
                && appointment.AppointmentId != excludeAppointmentId));

        public Task AddAsync(AppointmentEntity appointment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never creates an appointment.");

        public Task LockPatientAsync(Guid patientId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering takes the queue lock, not a patient's.");

        public Task<IReadOnlyList<AppointmentListing>> ListAsync(
            Guid? doctorId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never lists appointments.");

        public Task<IReadOnlyList<AppointmentListing>> ListUpcomingForPatientAsync(
            Guid patientId, DateTime nowUtc, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never lists appointments.");

        public Task<AppointmentEntity?> GetByAppointmentIdAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never reads one appointment.");

        public Task<AppointmentEntity?> GetTrackedForUpdateAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never locks an appointment.");
    }

    private sealed class FakeDoctorRepository : IDoctorCacheRepository
    {
        public HashSet<Guid> Active { get; } = [];

        public Task<DoctorCache?> GetActiveAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active.Contains(doctorId)
                ? new DoctorCache { DoctorId = doctorId, IsActive = true }
                : null);

        public Task<DoctorCache?> GetTrackedByDoctorIdAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never updates the doctor cache.");

        public Task<IReadOnlyList<DoctorCache>> ListActiveAsync(string? specialization, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never lists doctors.");

        public Task<IReadOnlyList<string>> ListSpecializationsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never lists specializations.");

        public Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Offering never adds doctors.");
    }

    /// <summary>
    /// Records, at every save, each entry's status and the slot's — what the database would hold
    /// at that moment.
    /// </summary>
    private sealed class FakeUnitOfWork(List<WaitlistEntry> entries, Slot slot) : IUnitOfWork
    {
        public List<Dictionary<WaitlistEntry, string>> Saves { get; } = [];
        public List<string> SlotStatusAtSave { get; } = [];
        public int SaveCount => Saves.Count;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves.Add(entries.ToDictionary(entry => entry, entry => entry.Status));
            SlotStatusAtSave.Add(slot.Status);
            return Task.CompletedTask;
        }

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The offerer runs inside its caller's transaction.");
    }
}
