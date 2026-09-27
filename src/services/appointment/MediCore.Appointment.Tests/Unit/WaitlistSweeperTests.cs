using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>
/// SCRUM-37 AC3 and the [QA] unit test "offer expiry": the sweep that lets an unanswered offer
/// lapse and pass down the queue, and the housekeeping around it. Driven through the real
/// <see cref="WaitlistOfferer"/> over an in-memory clinic, so the chain is exercised end to end.
/// </summary>
public sealed class WaitlistSweeperTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);   // 13:30 Colombo
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Start = Now.AddDays(2);
    private static readonly DateOnly Day = ColomboTime.ToColomboDate(Start);

    // ── Lapsed offers (AC3) ───────────────────────────────────────────────────

    [Fact]
    public async Task A_lapsed_offer_expires_and_passes_to_the_next_in_line()
    {
        var fixture = new Fixture();
        var slot = fixture.AddSlot(Start, SlotStatus.Offered);
        var holder = fixture.AddEntry(1, WaitlistStatus.Offered, offered: slot, expiresAtUtc: Now.AddMinutes(-1));
        var next = fixture.AddEntry(2, WaitlistStatus.Waiting);

        var summary = await fixture.SweepAsync();

        Assert.Equal(1, summary.OffersExpired);
        Assert.Equal(WaitlistStatus.Expired, holder.Status);
        Assert.Equal(Now, holder.ClosedAtUtc);
        Assert.Equal(WaitlistSweeper.Actor, holder.UpdatedBy);
        Assert.Equal(WaitlistStatus.Offered, next.Status);
        Assert.Equal(slot.SlotId, next.OfferedSlotId);
        Assert.Equal(Now.AddMinutes(60), next.OfferExpiresAtUtc);
        Assert.Equal(SlotStatus.Offered, slot.Status);
    }

    [Fact]
    public async Task Unanswered_offers_chain_down_the_queue_and_then_free_the_slot()
    {
        var fixture = new Fixture();
        var slot = fixture.AddSlot(Start, SlotStatus.Offered);
        var first = fixture.AddEntry(1, WaitlistStatus.Offered, offered: slot, expiresAtUtc: Now);
        var second = fixture.AddEntry(2, WaitlistStatus.Waiting);
        var third = fixture.AddEntry(3, WaitlistStatus.Waiting);

        await fixture.SweepAsync();
        Assert.Equal(WaitlistStatus.Offered, second.Status);

        fixture.Clock.Advance(TimeSpan.FromMinutes(60));
        await fixture.SweepAsync();
        Assert.Equal(WaitlistStatus.Expired, second.Status);
        Assert.Equal(WaitlistStatus.Offered, third.Status);

        fixture.Clock.Advance(TimeSpan.FromMinutes(60));
        await fixture.SweepAsync();

        Assert.Equal(
            [WaitlistStatus.Expired, WaitlistStatus.Expired, WaitlistStatus.Expired],
            [first.Status, second.Status, third.Status]);
        Assert.Equal(SlotStatus.Available, slot.Status);
    }

    [Fact]
    public async Task An_offer_lapses_at_the_instant_of_its_expiry()
    {
        var fixture = new Fixture();
        var slot = fixture.AddSlot(Start, SlotStatus.Offered);
        var holder = fixture.AddEntry(1, WaitlistStatus.Offered, offered: slot, expiresAtUtc: Now);

        await fixture.SweepAsync();

        Assert.Equal(WaitlistStatus.Expired, holder.Status);
    }

    [Fact]
    public async Task An_offer_still_open_is_left_alone()
    {
        var fixture = new Fixture();
        var slot = fixture.AddSlot(Start, SlotStatus.Offered);
        var holder = fixture.AddEntry(1, WaitlistStatus.Offered, offered: slot, expiresAtUtc: Now.AddSeconds(1));

        var summary = await fixture.SweepAsync();

        Assert.Equal(WaitlistStatus.Offered, holder.Status);
        Assert.False(summary.DidAnything);
    }

    [Fact]
    public async Task An_offer_accepted_after_it_was_listed_is_not_expired()
    {
        // The patient answered between the sweep's listing and its lock. The re-read wins.
        var fixture = new Fixture();
        var slot = fixture.AddSlot(Start, SlotStatus.Offered);
        var holder = fixture.AddEntry(1, WaitlistStatus.Offered, offered: slot, expiresAtUtc: Now);
        fixture.Waitlist.AfterLapsedListed = () =>
        {
            holder.Status = WaitlistStatus.Accepted;
            slot.Status = SlotStatus.Booked;
        };

        var summary = await fixture.SweepAsync();

        Assert.Equal(0, summary.OffersExpired);
        Assert.Equal(WaitlistStatus.Accepted, holder.Status);
        Assert.Equal(SlotStatus.Booked, slot.Status);
    }

    // ── Orphaned holds ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_hold_with_no_offer_behind_it_goes_back_to_the_queue()
    {
        var fixture = new Fixture();
        var slot = fixture.AddSlot(Start, SlotStatus.Offered);
        var waiting = fixture.AddEntry(1, WaitlistStatus.Waiting);

        var summary = await fixture.SweepAsync();

        Assert.Equal(1, summary.HoldsReleased);
        Assert.Equal(WaitlistStatus.Offered, waiting.Status);
        Assert.Equal(slot.SlotId, waiting.OfferedSlotId);
    }

    [Fact]
    public async Task A_hold_with_nobody_waiting_goes_back_on_the_public_list()
    {
        var fixture = new Fixture();
        var slot = fixture.AddSlot(Start, SlotStatus.Offered);

        await fixture.SweepAsync();

        Assert.Equal(SlotStatus.Available, slot.Status);
    }

    // ── Queues that can no longer be served ───────────────────────────────────

    [Fact]
    public async Task Entries_for_a_day_that_has_passed_expire()
    {
        var yesterday = ColomboTime.ToColomboDate(Now).AddDays(-1);
        var fixture = new Fixture();
        var entry = fixture.AddEntry(1, WaitlistStatus.Waiting, date: yesterday);

        var summary = await fixture.SweepAsync();

        Assert.Equal(1, summary.EntriesClosed);
        Assert.Equal(WaitlistStatus.Expired, entry.Status);
        Assert.Equal(WaitlistSweeper.DayPassedReason, entry.ClosedReason);
    }

    [Fact]
    public async Task A_queue_whose_doctor_has_left_is_withdrawn_and_its_hold_released()
    {
        var fixture = new Fixture();
        fixture.Doctors.Active.Clear();
        var slot = fixture.AddSlot(Start, SlotStatus.Offered);
        var holder = fixture.AddEntry(1, WaitlistStatus.Offered, offered: slot, expiresAtUtc: Now.AddMinutes(30));
        var waiting = fixture.AddEntry(2, WaitlistStatus.Waiting);

        var summary = await fixture.SweepAsync();

        Assert.Equal(2, summary.EntriesClosed);
        Assert.All([holder, waiting], entry =>
        {
            Assert.Equal(WaitlistStatus.Withdrawn, entry.Status);
            Assert.Equal(WaitlistSweeper.DoctorUnavailableReason, entry.ClosedReason);
        });
        Assert.Equal(SlotStatus.Available, slot.Status);
    }

    [Fact]
    public async Task A_queue_whose_clinic_was_cancelled_is_withdrawn()
    {
        // A holiday or approved leave removed every slot that day.
        var fixture = new Fixture();
        var entry = fixture.AddEntry(1, WaitlistStatus.Waiting);

        await fixture.SweepAsync();

        Assert.Equal(WaitlistStatus.Withdrawn, entry.Status);
        Assert.Equal(WaitlistSweeper.NoClinicReason, entry.ClosedReason);
    }

    [Fact]
    public async Task A_full_day_keeps_its_queue()
    {
        var fixture = new Fixture();
        fixture.AddSlot(Start, SlotStatus.Booked);
        var entry = fixture.AddEntry(1, WaitlistStatus.Waiting);

        var summary = await fixture.SweepAsync();

        Assert.Equal(WaitlistStatus.Waiting, entry.Status);
        Assert.False(summary.DidAnything);
    }

    // ── Free slots the release paths did not offer ────────────────────────────

    [Fact]
    public async Task A_slot_freed_by_unblocking_is_offered_to_the_queue()
    {
        var fixture = new Fixture();
        fixture.AddSlot(Start, SlotStatus.Booked);
        var freed = fixture.AddSlot(Start.AddMinutes(30), SlotStatus.Available);
        var waiting = fixture.AddEntry(1, WaitlistStatus.Waiting);

        var summary = await fixture.SweepAsync();

        Assert.Equal(1, summary.SlotsOffered);
        Assert.Equal(freed.SlotId, waiting.OfferedSlotId);
        Assert.Equal(SlotStatus.Offered, freed.Status);
    }

    [Fact]
    public async Task Two_free_slots_go_to_two_different_patients_in_order()
    {
        var fixture = new Fixture();
        var early = fixture.AddSlot(Start, SlotStatus.Available);
        var late = fixture.AddSlot(Start.AddMinutes(30), SlotStatus.Available);
        var first = fixture.AddEntry(1, WaitlistStatus.Waiting);
        var second = fixture.AddEntry(2, WaitlistStatus.Waiting);

        var summary = await fixture.SweepAsync();

        Assert.Equal(2, summary.SlotsOffered);
        Assert.Equal(early.SlotId, first.OfferedSlotId);
        Assert.Equal(late.SlotId, second.OfferedSlotId);
    }

    [Fact]
    public async Task More_free_slots_than_patients_leaves_the_rest_on_the_public_list()
    {
        var fixture = new Fixture();
        fixture.AddSlot(Start, SlotStatus.Available);
        var spare = fixture.AddSlot(Start.AddMinutes(30), SlotStatus.Available);
        fixture.AddEntry(1, WaitlistStatus.Waiting);

        var summary = await fixture.SweepAsync();

        Assert.Equal(1, summary.SlotsOffered);
        Assert.Equal(SlotStatus.Available, spare.Status);
    }

    // ── Robustness ────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_item_that_fails_is_left_for_the_next_pass_and_the_rest_go_ahead()
    {
        var fixture = new Fixture();
        var slotA = fixture.AddSlot(Start, SlotStatus.Offered);
        var slotB = fixture.AddSlot(Start.AddMinutes(30), SlotStatus.Offered);
        var holderA = fixture.AddEntry(1, WaitlistStatus.Offered, offered: slotA, expiresAtUtc: Now.AddMinutes(-2));
        var holderB = fixture.AddEntry(2, WaitlistStatus.Offered, offered: slotB, expiresAtUtc: Now.AddMinutes(-1));
        fixture.UnitOfWork.FailTransactions.Enqueue(true);

        var summary = await fixture.SweepAsync();

        Assert.Equal(1, summary.OffersExpired);
        Assert.Equal(WaitlistStatus.Offered, holderA.Status);
        Assert.Equal(WaitlistStatus.Expired, holderB.Status);
    }

    [Fact]
    public async Task A_quiet_waitlist_sweeps_to_nothing()
    {
        var summary = await new Fixture().SweepAsync();

        Assert.Equal(new WaitlistSweepSummary(0, 0, 0, 0), summary);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class Fixture
    {
        public Fixture()
        {
            Doctors.Active.Add(DoctorId);
            var offerer = new WaitlistOfferer(
                Waitlist,
                new NoOverlapAppointmentRepository(),
                Doctors,
                UnitOfWork,
                Options.Create(new WaitlistOptions { OfferWindowMinutes = 60, MinimumLeadMinutes = 15 }));
            Sweeper = new WaitlistSweeper(
                Waitlist,
                offerer,
                Waitlist,
                Doctors,
                UnitOfWork,
                Clock,
                NullLogger<WaitlistSweeper>.Instance);
        }

        public MutableTimeProvider Clock { get; } = new(Now);

        /// <summary>The waitlist and the slots, one in-memory store serving both repositories.</summary>
        public InMemoryClinic Waitlist { get; } = new();
        public FakeDoctorRepository Doctors { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public WaitlistSweeper Sweeper { get; }

        public Slot AddSlot(DateTime startUtc, string status)
        {
            var slot = new Slot
            {
                DoctorId = DoctorId,
                StartUtc = startUtc,
                EndUtc = startUtc.AddMinutes(30),
                SlotDate = ColomboTime.ToColomboDate(startUtc),
                DurationMinutes = 30,
                Status = status
            };
            Waitlist.SlotRows.Add(slot);
            return slot;
        }

        public WaitlistEntry AddEntry(
            int position,
            string status,
            Slot? offered = null,
            DateTime? expiresAtUtc = null,
            DateOnly? date = null)
        {
            var entry = new WaitlistEntry
            {
                DoctorId = DoctorId,
                SlotDate = date ?? Day,
                PatientId = Guid.NewGuid(),
                Position = position,
                Status = status,
                OfferedSlotId = offered?.SlotId,
                OfferExpiresAtUtc = expiresAtUtc
            };
            Waitlist.Entries.Add(entry);
            return entry;
        }

        public Task<WaitlistSweepSummary> SweepAsync() => Sweeper.SweepAsync();
    }

    /// <summary>
    /// The waitlist and the slots together, in memory, answering each query as the real
    /// repositories do. Reads hand out the live objects, as a change tracker would.
    /// </summary>
    private sealed class InMemoryClinic : IWaitlistRepository, ISlotRepository
    {
        public List<WaitlistEntry> Entries { get; } = [];
        public List<Slot> SlotRows { get; } = [];

        /// <summary>Runs once, after the sweep has listed the lapsed offers and before it locks.</summary>
        public Action? AfterLapsedListed { get; set; }

        private static bool Active(WaitlistEntry entry) => WaitlistStatus.IsActive(entry.Status);

        public Task LockQueueAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<WaitlistEntry>> GetLapsedOffersAsync(
            DateTime nowUtc, int limit, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<WaitlistEntry> lapsed = Entries
                .Where(entry => entry.Status == WaitlistStatus.Offered && entry.OfferExpiresAtUtc <= nowUtc)
                .OrderBy(entry => entry.OfferExpiresAtUtc)
                .Take(limit)
                .Select(Copy)
                .ToList();
            AfterLapsedListed?.Invoke();
            AfterLapsedListed = null;
            return Task.FromResult(lapsed);
        }

        public Task<IReadOnlyList<Slot>> GetOrphanedOfferedSlotsAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Slot>>(SlotRows
                .Where(slot => slot.Status == SlotStatus.Offered
                    && !Entries.Any(entry => entry.Status == WaitlistStatus.Offered && entry.OfferedSlotId == slot.SlotId))
                .Take(limit)
                .ToList());

        public Task<bool> HasOpenOfferForSlotAsync(Guid slotId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.Any(entry => entry.Status == WaitlistStatus.Offered && entry.OfferedSlotId == slotId));

        public Task<IReadOnlyList<WaitlistQueue>> GetActiveQueuesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WaitlistQueue>>(Entries
                .Where(Active)
                .Select(entry => new WaitlistQueue(entry.DoctorId, entry.SlotDate))
                .Distinct()
                .ToList());

        public Task<WaitlistEntry?> GetTrackedByEntryIdAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.SingleOrDefault(entry => entry.WaitlistEntryId == waitlistEntryId));

        public Task<IReadOnlyList<WaitlistEntry>> GetActiveTrackedAsync(
            Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WaitlistEntry>>(Entries
                .Where(entry => entry.DoctorId == doctorId && entry.SlotDate == date && Active(entry))
                .OrderBy(entry => entry.Position)
                .ToList());

        public Task<IReadOnlyList<WaitlistEntry>> GetWaitingTrackedAsync(
            Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WaitlistEntry>>(Entries
                .Where(entry => entry.DoctorId == doctorId && entry.SlotDate == date
                    && entry.Status == WaitlistStatus.Waiting)
                .OrderBy(entry => entry.Position)
                .ToList());

        public Task<bool> HasBookedWithDoctorOnAsync(
            Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<DayAvailability>> GetDayAvailabilityAsync(
            Guid doctorId, DateOnly from, DateOnly to, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DayAvailability>>(SlotRows
                .Where(slot => slot.DoctorId == doctorId && slot.SlotDate >= from && slot.SlotDate <= to
                    && slot.StartUtc >= nowUtc
                    && slot.Status is SlotStatus.Available or SlotStatus.Booked or SlotStatus.Offered)
                .GroupBy(slot => slot.SlotDate)
                .Select(day => new DayAvailability(
                    day.Key,
                    day.Count(slot => slot.Status == SlotStatus.Available),
                    day.Count(slot => slot.Status != SlotStatus.Available)))
                .ToList());

        public Task<Slot?> GetTrackedBySlotIdAsync(Guid slotId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SlotRows.SingleOrDefault(slot => slot.SlotId == slotId));

        public Task<IReadOnlyList<Slot>> GetAvailableAsync(
            Guid doctorId, DateOnly from, DateOnly to, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Slot>>(SlotRows
                .Where(slot => slot.DoctorId == doctorId && slot.SlotDate >= from && slot.SlotDate <= to
                    && slot.Status == SlotStatus.Available && slot.StartUtc >= nowUtc)
                .OrderBy(slot => slot.StartUtc)
                .ToList());

        /// <summary>An untracked read is a copy: changing it changes nothing stored.</summary>
        private static WaitlistEntry Copy(WaitlistEntry entry) => new()
        {
            WaitlistEntryId = entry.WaitlistEntryId,
            DoctorId = entry.DoctorId,
            SlotDate = entry.SlotDate,
            PatientId = entry.PatientId,
            Position = entry.Position,
            Status = entry.Status,
            OfferedSlotId = entry.OfferedSlotId,
            OfferExpiresAtUtc = entry.OfferExpiresAtUtc
        };

        // ── Not used by the sweep ──────────────────────────────────────────────

        public Task<int> GetNextPositionAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sweep never joins anyone.");

        public Task<bool> HasActiveEntryAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sweep never joins anyone.");

        public Task<int> CountActiveForPatientAsync(Guid patientId, DateOnly from, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sweep never joins anyone.");

        public Task<IReadOnlyList<WaitlistEntry>> GetTrackedOfferedForSlotsAsync(
            IReadOnlyCollection<Guid> slotIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only schedule revision puts offers back.");

        public Task<WaitlistEntry?> GetByEntryIdAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sweep reads entries under the lock.");

        public Task AddAsync(WaitlistEntry entry, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sweep never joins anyone.");

        public Task<WaitlistListing?> GetListingAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sweep never lists.");

        public Task<IReadOnlyList<WaitlistListing>> ListForPatientAsync(
            Guid patientId, DateTime closedSince, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sweep never lists.");

        public Task<IReadOnlyList<WaitlistListing>> ListAsync(
            Guid? doctorId, DateOnly from, DateOnly to, IReadOnlyCollection<string>? statuses,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sweep never lists.");

        public Task<IReadOnlyList<WaitingPosition>> GetWaitingPositionsAsync(
            IReadOnlyCollection<Guid> doctorIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sweep never lists.");

        public Task<IReadOnlyList<Slot>> GetTrackedForDoctorBetweenAsync(
            Guid doctorId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sweep never reconciles.");

        public Task AddRangeAsync(IReadOnlyCollection<Slot> slots, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sweep never creates slots.");

        public void RemoveRange(IReadOnlyCollection<Slot> slots) =>
            throw new NotSupportedException("The sweep never deletes slots.");

        public Task<IReadOnlyList<Slot>> GetFlaggedAsync(Guid? doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The sweep never reads the attention list.");
    }

    private sealed class NoOverlapAppointmentRepository : IAppointmentRepository
    {
        public Task<AppointmentEntity?> FindPatientOverlapAsync(
            Guid patientId, DateTime startUtc, DateTime endUtc, Guid? excludeAppointmentId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AppointmentEntity?>(null);

        public Task AddAsync(AppointmentEntity appointment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task LockPatientAsync(Guid patientId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AppointmentListing>> ListAsync(
            Guid? doctorId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AppointmentListing>> ListUpcomingForPatientAsync(
            Guid patientId, DateTime nowUtc, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppointmentEntity?> GetByAppointmentIdAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppointmentEntity?> GetTrackedForUpdateAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDoctorRepository : IDoctorCacheRepository
    {
        public HashSet<Guid> Active { get; } = [];

        public Task<DoctorCache?> GetActiveAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active.Contains(doctorId) ? new DoctorCache { DoctorId = doctorId, IsActive = true } : null);

        public Task<DoctorCache?> GetTrackedByDoctorIdAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DoctorCache>> ListActiveAsync(string? specialization, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListSpecializationsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Runs each item as a transaction. A queued <c>true</c> makes that transaction fail as a lost
    /// race would, after snapshotting and before any change is kept.
    /// </summary>
    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Queue<bool> FailTransactions { get; } = new();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default)
        {
            return FailTransactions.TryDequeue(out var fail) && fail
                ? Task.FromException<T>(new Application.Exceptions.ConcurrentUpdateException())
                : work(cancellationToken);
        }
    }

    private sealed class MutableTimeProvider(DateTime utcNow) : TimeProvider
    {
        private DateTime _utcNow = utcNow;

        public void Advance(TimeSpan by) => _utcNow += by;

        public override DateTimeOffset GetUtcNow() => new(_utcNow);
    }
}
