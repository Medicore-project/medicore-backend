using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using Microsoft.Extensions.Options;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>SCRUM-37: joining a waitlist (AC1) and reading it back.</summary>
public sealed class WaitlistServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);   // 13:30 in Colombo
    private static readonly DateOnly Today = new(2026, 9, 23);
    private static readonly DateOnly Day = new(2026, 9, 25);
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherDoctorId = Guid.Parse("11111111-1111-1111-1111-222222222222");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly BookingPatientDetails Details = new("PAT-000042", "Nimal Perera");

    // ── Joining (AC1) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Joining_a_full_day_puts_the_patient_at_the_back_of_the_queue()
    {
        var fixture = new Fixture();
        fixture.Waitlist.Entries.Add(Entry(1, WaitlistStatus.Expired));
        fixture.Waitlist.Entries.Add(Entry(2, WaitlistStatus.Waiting));
        fixture.Waitlist.Entries.Add(Entry(3, WaitlistStatus.Waiting));

        var result = await fixture.JoinAsync();

        var joined = Assert.IsType<WaitlistJoinedResult>(result).Entry;
        Assert.Equal(4, joined.Position);
        Assert.Equal(3, joined.PlaceInLine);
        Assert.Equal(WaitlistStatus.Waiting, joined.Status);

        var stored = Assert.Single(fixture.Waitlist.Added);
        Assert.Equal(DoctorId, stored.DoctorId);
        Assert.Equal(Day, stored.SlotDate);
        Assert.Equal(PatientId, stored.PatientId);
        Assert.Equal("PAT-000042", stored.PatientNumber);
        Assert.Equal("Nimal Perera", stored.PatientName);
        Assert.Equal(ServiceCodes.GeneralConsultation, stored.ServiceCode);
        Assert.Equal(Now, stored.JoinedAtUtc);
        Assert.Equal("patient", stored.CreatedBy);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_position_is_never_handed_out_twice_even_after_everyone_ahead_has_left()
    {
        // The queue once reached 5; everyone has gone. The newcomer is first in line, but at
        // position 6 — the stored order only ever grows.
        var fixture = new Fixture();
        fixture.Waitlist.Entries.Add(Entry(5, WaitlistStatus.Withdrawn));

        var joined = Assert.IsType<WaitlistJoinedResult>(await fixture.JoinAsync()).Entry;

        Assert.Equal(6, joined.Position);
        Assert.Equal(1, joined.PlaceInLine);
    }

    [Fact]
    public async Task Another_doctors_queue_does_not_affect_the_position()
    {
        var fixture = new Fixture();
        fixture.Waitlist.Entries.Add(Entry(7, WaitlistStatus.Waiting, doctorId: OtherDoctorId));
        fixture.Waitlist.Entries.Add(Entry(9, WaitlistStatus.Waiting, date: Day.AddDays(1)));

        var joined = Assert.IsType<WaitlistJoinedResult>(await fixture.JoinAsync()).Entry;

        Assert.Equal(1, joined.Position);
        Assert.Equal(1, joined.PlaceInLine);
    }

    [Fact]
    public async Task The_queue_is_locked_before_the_day_or_the_queue_is_read()
    {
        var fixture = new Fixture();

        await fixture.JoinAsync();

        Assert.Equal(
            [
                "lock queue", "read day", "check active", "check booked", "count active", "next position", "add",
                "read positions"   // the read-back, for the new entry's place in line
            ],
            fixture.Waitlist.Log);
    }

    [Fact]
    public async Task The_chosen_service_code_is_kept_for_the_appointment_an_offer_creates()
    {
        var fixture = new Fixture();

        await fixture.JoinAsync(serviceCode: ServiceCodes.FollowUp);

        Assert.Equal(ServiceCodes.FollowUp, Assert.Single(fixture.Waitlist.Added).ServiceCode);
    }

    [Fact]
    public async Task A_day_with_free_times_is_not_full_and_cannot_be_joined()
    {
        var fixture = new Fixture();
        fixture.Waitlist.Days[Day] = new DayAvailability(Day, Free: 2, Taken: 10);

        var result = await fixture.JoinAsync();

        Assert.Equal(2, Assert.IsType<WaitlistDayNotFullResult>(result).FreeSlots);
        Assert.Empty(fixture.Waitlist.Added);
    }

    [Fact]
    public async Task A_day_without_a_clinic_cannot_be_joined()
    {
        // Not working, on leave, every slot blocked, or the clinic already over: nothing to wait for.
        var fixture = new Fixture();
        fixture.Waitlist.Days.Clear();

        Assert.IsType<WaitlistNoClinicThatDayResult>(await fixture.JoinAsync());
        Assert.Empty(fixture.Waitlist.Added);
    }

    [Fact]
    public async Task A_past_day_cannot_be_joined()
    {
        var fixture = new Fixture();

        Assert.IsType<WaitlistDateInPastResult>(await fixture.JoinAsync(date: Today.AddDays(-1)));
        Assert.Empty(fixture.Waitlist.Log);
    }

    [Fact]
    public async Task Today_can_be_joined_while_its_clinic_is_still_to_come()
    {
        var fixture = new Fixture();
        fixture.Waitlist.Days[Today] = new DayAvailability(Today, Free: 0, Taken: 3);

        Assert.IsType<WaitlistJoinedResult>(await fixture.JoinAsync(date: Today));
    }

    [Fact]
    public async Task A_day_past_the_slot_horizon_cannot_be_joined()
    {
        // No slots exist that far out, so the day cannot be full; once they do, the patient books.
        var fixture = new Fixture(horizonDays: 60);

        var result = await fixture.JoinAsync(date: Today.AddDays(61));

        Assert.Equal(60, Assert.IsType<WaitlistBeyondHorizonResult>(result).HorizonDays);
    }

    [Fact]
    public async Task The_last_day_of_the_horizon_can_be_joined()
    {
        var last = Today.AddDays(60);
        var fixture = new Fixture(horizonDays: 60);
        fixture.Waitlist.Days[last] = new DayAvailability(last, Free: 0, Taken: 1);

        Assert.IsType<WaitlistJoinedResult>(await fixture.JoinAsync(date: last));
    }

    [Fact]
    public async Task An_unknown_or_inactive_doctor_is_refused_before_any_lock()
    {
        var fixture = new Fixture();
        fixture.Doctors.Active.Clear();

        Assert.IsType<WaitlistDoctorNotFoundResult>(await fixture.JoinAsync());
        Assert.Empty(fixture.Waitlist.Log);
    }

    [Fact]
    public async Task A_patient_already_in_the_queue_cannot_join_it_again()
    {
        var fixture = new Fixture();
        fixture.Waitlist.Entries.Add(Entry(1, WaitlistStatus.Offered, patientId: PatientId));

        Assert.IsType<WaitlistAlreadyWaitingResult>(await fixture.JoinAsync());
        Assert.Empty(fixture.Waitlist.Added);
    }

    [Fact]
    public async Task A_patient_whose_earlier_entry_closed_can_rejoin_at_the_back()
    {
        var fixture = new Fixture();
        fixture.Waitlist.Entries.Add(Entry(1, WaitlistStatus.Expired, patientId: PatientId));

        var joined = Assert.IsType<WaitlistJoinedResult>(await fixture.JoinAsync()).Entry;

        Assert.Equal(2, joined.Position);
    }

    [Fact]
    public async Task A_patient_already_booked_with_the_doctor_that_day_cannot_join()
    {
        var fixture = new Fixture();
        fixture.Waitlist.Booked.Add((PatientId, DoctorId, Day));

        Assert.IsType<WaitlistAlreadyBookedResult>(await fixture.JoinAsync());
        Assert.Empty(fixture.Waitlist.Added);
    }

    [Fact]
    public async Task A_patient_waiting_in_as_many_queues_as_allowed_cannot_join_another()
    {
        var fixture = new Fixture(maxActive: 2);
        fixture.Waitlist.Entries.Add(Entry(1, WaitlistStatus.Waiting, patientId: PatientId, doctorId: OtherDoctorId));
        fixture.Waitlist.Entries.Add(Entry(1, WaitlistStatus.Offered, patientId: PatientId, date: Day.AddDays(3)));

        var result = await fixture.JoinAsync();

        Assert.Equal(2, Assert.IsType<WaitlistTooManyEntriesResult>(result).Limit);
    }

    [Fact]
    public async Task Entries_for_days_already_gone_do_not_count_against_the_limit()
    {
        // The sweeper may not have closed yesterday's entry yet.
        var fixture = new Fixture(maxActive: 1);
        fixture.Waitlist.Entries.Add(Entry(1, WaitlistStatus.Waiting, patientId: PatientId, date: Today.AddDays(-1)));

        Assert.IsType<WaitlistJoinedResult>(await fixture.JoinAsync());
        Assert.Equal(Today, fixture.Waitlist.CountedFrom);
    }

    [Fact]
    public async Task The_active_entry_index_firing_is_answered_as_already_waiting()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.Outcomes.Enqueue(new DuplicateWaitlistEntryException());

        Assert.IsType<WaitlistAlreadyWaitingResult>(await fixture.JoinAsync());
        Assert.Equal(0, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_lost_position_race_rereads_the_queue_and_joins_on_the_retry()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        var result = await fixture.JoinAsync();

        Assert.IsType<WaitlistJoinedResult>(result);
        Assert.Equal(2, fixture.UnitOfWork.Transactions);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Three_lost_races_in_a_row_ask_the_caller_to_try_again()
    {
        var fixture = new Fixture();
        for (var i = 0; i < 3; i++)
        {
            fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());
        }

        Assert.IsType<WaitlistJoinContendedResult>(await fixture.JoinAsync());
        Assert.Equal(3, fixture.UnitOfWork.Transactions);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_gives_each_waiting_entry_its_place_and_nobody_else_one()
    {
        var fixture = new Fixture();
        fixture.Waitlist.Entries.Add(Entry(1, WaitlistStatus.Accepted));
        fixture.Waitlist.Entries.Add(Entry(2, WaitlistStatus.Offered));
        fixture.Waitlist.Entries.Add(Entry(4, WaitlistStatus.Waiting));
        fixture.Waitlist.Entries.Add(Entry(6, WaitlistStatus.Waiting));

        var list = await fixture.Service.ListAsync(DoctorId, Day, Day, status: null);

        Assert.Equal([null, null, 1, 2], list.Select(entry => entry.PlaceInLine));
    }

    [Fact]
    public async Task The_active_filter_asks_for_waiting_and_offered_entries()
    {
        var fixture = new Fixture();

        await fixture.Service.ListAsync(null, Today, Day, IWaitlistService.ActiveFilter);

        Assert.Equal([WaitlistStatus.Waiting, WaitlistStatus.Offered], fixture.Waitlist.ListedStatuses);
    }

    [Fact]
    public async Task One_status_filters_to_that_status_and_none_filters_nothing()
    {
        var fixture = new Fixture();

        await fixture.Service.ListAsync(null, Today, Day, WaitlistStatus.Expired);
        Assert.Equal([WaitlistStatus.Expired], fixture.Waitlist.ListedStatuses);

        await fixture.Service.ListAsync(null, Today, Day, status: null);
        Assert.Null(fixture.Waitlist.ListedStatuses);
    }

    [Fact]
    public async Task A_list_with_nobody_waiting_does_not_read_the_queue_positions()
    {
        var fixture = new Fixture();
        fixture.Waitlist.Entries.Add(Entry(1, WaitlistStatus.Accepted));

        await fixture.Service.ListAsync(DoctorId, Day, Day, status: null);

        Assert.DoesNotContain("read positions", fixture.Waitlist.Log);
    }

    [Fact]
    public async Task A_patient_sees_their_entries_closed_in_the_last_30_days()
    {
        var fixture = new Fixture();

        await fixture.Service.GetMineAsync(PatientId);

        Assert.Equal(Now.AddDays(-30), fixture.Waitlist.ClosedSince);
    }

    [Fact]
    public async Task An_unknown_entry_reads_as_null()
    {
        var fixture = new Fixture();

        Assert.Null(await fixture.Service.GetAsync(Guid.NewGuid()));
    }

    // ── Days, for the public booking page ────────────────────────────────────

    [Fact]
    public async Task Days_are_marked_full_or_not_and_come_soonest_first()
    {
        var fixture = new Fixture();
        fixture.Waitlist.Days.Clear();
        fixture.Waitlist.Days[Day.AddDays(1)] = new DayAvailability(Day.AddDays(1), Free: 3, Taken: 2);
        fixture.Waitlist.Days[Day] = new DayAvailability(Day, Free: 0, Taken: 12);

        var result = await fixture.Service.GetDaysAsync(DoctorId, null, null);

        var days = Assert.IsType<WaitlistDaysFoundResult>(result).Days;
        Assert.Equal([new PublicBookingDayResponse(Day, true), new PublicBookingDayResponse(Day.AddDays(1), false)], days);
    }

    [Fact]
    public async Task Days_default_to_today_through_the_horizon_and_never_look_past_it()
    {
        var fixture = new Fixture(horizonDays: 60);

        await fixture.Service.GetDaysAsync(DoctorId, Today.AddDays(-10), Today.AddDays(400));

        Assert.Equal((Today, Today.AddDays(60)), fixture.Waitlist.DaysAskedFor);
    }

    [Fact]
    public async Task A_range_inside_the_horizon_is_used_as_given()
    {
        var fixture = new Fixture();

        await fixture.Service.GetDaysAsync(DoctorId, Day, Day.AddDays(6));

        Assert.Equal((Day, Day.AddDays(6)), fixture.Waitlist.DaysAskedFor);
    }

    [Fact]
    public async Task A_range_wholly_outside_the_horizon_is_empty_without_a_query()
    {
        var fixture = new Fixture(horizonDays: 60);

        var result = await fixture.Service.GetDaysAsync(DoctorId, Today.AddDays(70), Today.AddDays(80));

        Assert.Empty(Assert.IsType<WaitlistDaysFoundResult>(result).Days);
        Assert.Null(fixture.Waitlist.DaysAskedFor);
    }

    [Fact]
    public async Task Days_for_a_doctor_who_is_not_bookable_are_refused()
    {
        var fixture = new Fixture();
        fixture.Doctors.Active.Clear();

        Assert.IsType<WaitlistDaysDoctorNotFoundResult>(await fixture.Service.GetDaysAsync(DoctorId, null, null));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WaitlistEntry Entry(
        int position,
        string status,
        Guid? patientId = null,
        Guid? doctorId = null,
        DateOnly? date = null) => new()
    {
        DoctorId = doctorId ?? DoctorId,
        SlotDate = date ?? Day,
        PatientId = patientId ?? Guid.NewGuid(),
        Position = position,
        Status = status,
        JoinedAtUtc = Now.AddHours(-position)
    };

    private sealed class Fixture
    {
        public FakeWaitlistRepository Waitlist { get; } = new();
        public FakeDoctorRepository Doctors { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public WaitlistService Service { get; }

        public Fixture(int horizonDays = 60, int maxActive = 3)
        {
            Doctors.Active.Add(DoctorId);
            UnitOfWork.RolledBack = Waitlist.Rollback;
            Waitlist.Days[Day] = new DayAvailability(Day, Free: 0, Taken: 12);

            Service = new WaitlistService(
                Waitlist,
                Doctors,
                UnitOfWork,
                new FixedTimeProvider(Now),
                Options.Create(new WaitlistOptions { MaxActiveEntriesPerPatient = maxActive }),
                Options.Create(new SchedulingOptions { SlotHorizonDays = horizonDays }));
        }

        public Task<WaitlistJoinResult> JoinAsync(DateOnly? date = null, string? serviceCode = null) =>
            Service.JoinAsync(DoctorId, date ?? Day, PatientId, Details, serviceCode, "patient");
    }

    private sealed class FakeWaitlistRepository : IWaitlistRepository
    {
        public List<WaitlistEntry> Entries { get; } = [];
        public List<WaitlistEntry> Added { get; } = [];
        public Dictionary<DateOnly, DayAvailability> Days { get; } = [];
        public HashSet<(Guid PatientId, Guid DoctorId, DateOnly Date)> Booked { get; } = [];
        public List<string> Log { get; } = [];
        public DateOnly? CountedFrom { get; private set; }
        public (DateOnly From, DateOnly To)? DaysAskedFor { get; private set; }
        public DateTime? ClosedSince { get; private set; }
        public IReadOnlyCollection<string>? ListedStatuses { get; private set; }

        public Task LockQueueAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken = default)
        {
            Log.Add("lock queue");
            return Task.CompletedTask;
        }

        public Task<int> GetNextPositionAsync(Guid doctorId, DateOnly date, CancellationToken cancellationToken = default)
        {
            // The contract: closed (and deleted) entries included.
            Log.Add("next position");
            var highest = Entries
                .Where(entry => entry.DoctorId == doctorId && entry.SlotDate == date)
                .Select(entry => (int?)entry.Position)
                .Max();
            return Task.FromResult((highest ?? 0) + 1);
        }

        public Task<bool> HasActiveEntryAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken = default)
        {
            Log.Add("check active");
            return Task.FromResult(Entries.Any(entry =>
                entry.PatientId == patientId && entry.DoctorId == doctorId && entry.SlotDate == date
                && WaitlistStatus.IsActive(entry.Status)));
        }

        public Task<int> CountActiveForPatientAsync(Guid patientId, DateOnly from, CancellationToken cancellationToken = default)
        {
            Log.Add("count active");
            CountedFrom = from;
            return Task.FromResult(Entries.Count(entry =>
                entry.PatientId == patientId && entry.SlotDate >= from && WaitlistStatus.IsActive(entry.Status)));
        }

        public Task<bool> HasBookedWithDoctorOnAsync(Guid patientId, Guid doctorId, DateOnly date, CancellationToken cancellationToken = default)
        {
            Log.Add("check booked");
            return Task.FromResult(Booked.Contains((patientId, doctorId, date)));
        }

        public Task<IReadOnlyList<DayAvailability>> GetDayAvailabilityAsync(
            Guid doctorId, DateOnly from, DateOnly to, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            Log.Add("read day");
            DaysAskedFor = (from, to);
            IReadOnlyList<DayAvailability> days = Days.Values
                .Where(day => day.Date >= from && day.Date <= to)
                .ToList();
            return Task.FromResult(days);
        }

        public Task<WaitlistEntry?> GetByEntryIdAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Joining and reading never change an existing entry.");

        public Task<WaitlistEntry?> GetTrackedByEntryIdAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Joining and reading never change an existing entry.");

        public Task<IReadOnlyList<WaitlistEntry>> GetWaitingTrackedAsync(
            Guid doctorId, DateOnly date, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Joining and reading never pick a candidate for an offer.");

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

        public Task AddAsync(WaitlistEntry entry, CancellationToken cancellationToken = default)
        {
            Log.Add("add");
            Added.Add(entry);
            Entries.Add(entry);
            _uncommitted.Add(entry);
            return Task.CompletedTask;
        }

        private readonly List<WaitlistEntry> _uncommitted = [];

        /// <summary>A rolled-back attempt's inserts never happened.</summary>
        public void Rollback()
        {
            Entries.RemoveAll(_uncommitted.Contains);
            _uncommitted.Clear();
        }

        public Task<WaitlistListing?> GetListingAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default)
        {
            var entry = Entries.SingleOrDefault(candidate => candidate.WaitlistEntryId == waitlistEntryId);
            return Task.FromResult(entry is null ? null : Listing(entry));
        }

        public Task<IReadOnlyList<WaitlistListing>> ListForPatientAsync(
            Guid patientId, DateTime closedSince, CancellationToken cancellationToken = default)
        {
            ClosedSince = closedSince;
            IReadOnlyList<WaitlistListing> listings = Entries
                .Where(entry => entry.PatientId == patientId)
                .Select(Listing)
                .ToList();
            return Task.FromResult(listings);
        }

        public Task<IReadOnlyList<WaitlistListing>> ListAsync(
            Guid? doctorId, DateOnly from, DateOnly to, IReadOnlyCollection<string>? statuses,
            CancellationToken cancellationToken = default)
        {
            ListedStatuses = statuses;
            IReadOnlyList<WaitlistListing> listings = Entries
                .Where(entry => entry.SlotDate >= from && entry.SlotDate <= to)
                .Where(entry => doctorId is null || entry.DoctorId == doctorId)
                .Where(entry => statuses is null || statuses.Contains(entry.Status))
                .OrderBy(entry => entry.Position)
                .Select(Listing)
                .ToList();
            return Task.FromResult(listings);
        }

        public Task<IReadOnlyList<WaitingPosition>> GetWaitingPositionsAsync(
            IReadOnlyCollection<Guid> doctorIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Log.Add("read positions");
            IReadOnlyList<WaitingPosition> positions = Entries
                .Where(entry => doctorIds.Contains(entry.DoctorId)
                    && entry.SlotDate >= from && entry.SlotDate <= to
                    && entry.Status == WaitlistStatus.Waiting)
                .Select(entry => new WaitingPosition(entry.DoctorId, entry.SlotDate, entry.Position))
                .ToList();
            return Task.FromResult(positions);
        }

        private static WaitlistListing Listing(WaitlistEntry entry) =>
            new(entry, "Dr. Silva", "Cardiology", null, null);
    }

    private sealed class FakeDoctorRepository : IDoctorCacheRepository
    {
        public HashSet<Guid> Active { get; } = [];

        public Task<DoctorCache?> GetActiveAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Active.Contains(doctorId)
                ? new DoctorCache { DoctorId = doctorId, FullName = "Dr. Silva", IsActive = true }
                : null);

        public Task<DoctorCache?> GetTrackedByDoctorIdAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Joining never updates the doctor cache.");

        public Task<IReadOnlyList<DoctorCache>> ListActiveAsync(string? specialization, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Joining never lists doctors.");

        public Task<IReadOnlyList<string>> ListSpecializationsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Joining never lists specializations.");

        public Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Joining never adds doctors.");
    }

    /// <summary>
    /// Runs the work as a transaction would. Each queued outcome is thrown by the next save in
    /// place of committing; a null lets that save through.
    /// </summary>
    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Queue<Exception?> Outcomes { get; } = new();
        public int SaveCount { get; private set; }
        public int Transactions { get; private set; }
        public int Commits { get; private set; }
        public Action? RolledBack { get; set; }

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
            try
            {
                var result = await work(cancellationToken);
                Commits++;
                return result;
            }
            catch
            {
                RolledBack?.Invoke();
                throw;
            }
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
