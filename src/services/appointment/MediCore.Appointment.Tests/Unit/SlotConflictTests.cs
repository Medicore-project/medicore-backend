using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>
/// SCRUM-35: the slot writers other than booking, when their save loses to a concurrent write on
/// the same slot row. Each fake read hands out a fresh <see cref="Slot"/> in the next state, the
/// way the real repository re-reads after the unit of work has cleared the change tracker.
/// </summary>
public sealed class SlotConflictTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 4, 0, 0, DateTimeKind.Utc);
    private static readonly Guid DoctorId = Guid.NewGuid();
    private static readonly Guid SlotId = Guid.NewGuid();

    // ── Blocking and unblocking ───────────────────────────────────────────────

    [Fact]
    public async Task Blocking_a_slot_that_was_booked_meanwhile_reports_it_booked_instead_of_overwriting()
    {
        // The admin read Available; a patient booked before the admin's save landed.
        var fixture = new Fixture(SlotStatus.Available, SlotStatus.Booked);
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        var result = await fixture.Slots.BlockAsync(SlotId, new BlockSlotRequest("Equipment"), "admin");

        var refused = Assert.IsType<SlotBlockNotAvailableResult>(result);
        Assert.Equal(SlotStatus.Booked, refused.CurrentStatus);
        // One save, which lost. The second attempt saw Booked and never tried to write.
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Blocking_goes_through_on_retry_when_the_slot_is_still_free()
    {
        // Something else touched the row — not a booking — so the block is still right.
        var fixture = new Fixture(SlotStatus.Available, SlotStatus.Available);
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        var result = await fixture.Slots.BlockAsync(SlotId, new BlockSlotRequest("Equipment"), "admin");

        var blocked = Assert.IsType<SlotBlockedResult>(result);
        Assert.Equal(SlotStatus.Blocked, blocked.Slot.Status);
        Assert.Equal(2, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Unblocking_a_slot_someone_else_already_unblocked_reports_its_current_state()
    {
        var fixture = new Fixture(SlotStatus.Blocked, SlotStatus.Available);
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        var result = await fixture.Slots.UnblockAsync(SlotId, "admin");

        var refused = Assert.IsType<SlotUnblockNotBlockedResult>(result);
        Assert.Equal(SlotStatus.Available, refused.CurrentStatus);
    }

    // ── Reconciliation ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_slot_booked_during_reconciliation_is_flagged_on_retry_instead_of_deleted()
    {
        // The schedule no longer covers the slot. Reconciliation read it Available and planned a
        // delete; a booking committed first, so the delete lost. Without the token and the retry,
        // the booked slot would be hard-deleted from under its appointment.
        var fixture = new Fixture(SlotStatus.Available, SlotStatus.Booked);
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        var summary = await fixture.Revisions.ReconcileDoctorAsync(DoctorId, "Schedule removed.");

        Assert.Equal(0, summary.SlotsRemoved);
        Assert.Equal(1, summary.SlotsFlagged);
        Assert.Equal(2, fixture.UnitOfWork.SaveCount);
        // The second attempt read a fresh slot and flagged that one.
        Assert.Equal(SlotStatus.Flagged, fixture.SlotRepository.Handed[^1].Status);
    }

    [Fact]
    public async Task The_clinic_wide_reconciliation_retries_the_same_way()
    {
        var fixture = new Fixture(SlotStatus.Available, SlotStatus.Booked);
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        var summary = await fixture.Revisions.ReconcileAllDoctorsAsync("Holiday.");

        Assert.Equal(0, summary.SlotsRemoved);
        Assert.Equal(1, summary.SlotsFlagged);
    }

    [Fact]
    public async Task Reconciliation_that_keeps_losing_lets_the_conflict_escape_for_a_409()
    {
        var fixture = new Fixture(SlotStatus.Available, SlotStatus.Available, SlotStatus.Available);
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());
        fixture.UnitOfWork.Outcomes.Enqueue(new ConcurrentUpdateException());

        await Assert.ThrowsAsync<ConcurrentUpdateException>(() =>
            fixture.Revisions.ReconcileDoctorAsync(DoctorId, "Schedule removed."));

        Assert.Equal(3, fixture.UnitOfWork.SaveCount);
    }

    // ── Fixture and fakes ─────────────────────────────────────────────────────

    private sealed class Fixture
    {
        public Fixture(params string[] statusOnEachRead)
        {
            SlotRepository = new FakeSlotRepository(statusOnEachRead);
            var clock = new FixedTimeProvider(Now);
            var generator = new FakeSlotGenerator();

            Slots = new SlotService(
                SlotRepository,
                new NoDoctorRepository(),
                generator,
                UnitOfWork,
                clock);
            Revisions = new ScheduleRevisionService(
                new NoScheduleRepository(),
                SlotRepository,
                new NoHolidayRepository(),
                new NoLeaveRepository(),
                generator,
                new SlotReconciler(),
                UnitOfWork,
                clock,
                NullLogger<ScheduleRevisionService>.Instance);
        }

        public FakeSlotRepository SlotRepository { get; }
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public SlotService Slots { get; }
        public ScheduleRevisionService Revisions { get; }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        /// <summary>What each save does in turn; null or an empty queue means it succeeds.</summary>
        public Queue<Exception?> Outcomes { get; } = new();
        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Outcomes.TryDequeue(out var outcome) && outcome is not null
                ? Task.FromException(outcome)
                : Task.CompletedTask;
        }

        // These services never open an explicit transaction; running the work directly keeps the
        // fake honest about that without pretending to roll anything back.
        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default) =>
            work(cancellationToken);
    }

    private sealed class FakeSlotRepository : ISlotRepository
    {
        private readonly Queue<string> _statusOnEachRead;

        public FakeSlotRepository(IEnumerable<string> statusOnEachRead)
        {
            _statusOnEachRead = new Queue<string>(statusOnEachRead);
        }

        /// <summary>Every slot handed out, in order — one per read.</summary>
        public List<Slot> Handed { get; } = [];

        private Slot Next()
        {
            var slot = new Slot
            {
                SlotId = SlotId,
                DoctorId = DoctorId,
                StartUtc = new DateTime(2026, 9, 28, 3, 30, 0, DateTimeKind.Utc),
                EndUtc = new DateTime(2026, 9, 28, 3, 45, 0, DateTimeKind.Utc),
                SlotDate = new DateOnly(2026, 9, 28),
                DurationMinutes = 15,
                Status = _statusOnEachRead.Dequeue()
            };
            Handed.Add(slot);
            return slot;
        }

        public Task<Slot?> GetTrackedBySlotIdAsync(Guid slotId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Slot?>(Next());

        public Task<IReadOnlyList<Slot>> GetTrackedForDoctorBetweenAsync(
            Guid doctorId,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Slot>>([Next()]);

        public Task AddRangeAsync(IReadOnlyCollection<Slot> slots, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void RemoveRange(IReadOnlyCollection<Slot> slots) { }

        public Task<IReadOnlyList<Slot>> GetAvailableAsync(
            Guid doctorId,
            DateOnly from,
            DateOnly to,
            DateTime nowUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Slot>> GetFlaggedAsync(Guid? doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>The doctor has no schedules left, so every stored slot is surplus.</summary>
    private sealed class NoScheduleRepository : IDoctorScheduleRepository
    {
        public Task<IReadOnlyList<DoctorSchedule>> GetActiveForDoctorAsync(
            Guid doctorId,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoctorSchedule>>([]);

        public Task<IReadOnlyList<Guid>> GetDoctorIdsWithActiveSchedulesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([DoctorId]);

        public Task<IReadOnlyList<DoctorSchedule>> GetForOverlapCheckAsync(
            Guid doctorId,
            DayOfWeek dayOfWeek,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DoctorSchedule>> GetAllForDoctorAsync(
            Guid doctorId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DoctorSchedule?> GetByScheduleIdAsync(Guid scheduleId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DoctorSchedule?> GetTrackedByScheduleIdAsync(
            Guid scheduleId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(DoctorSchedule schedule, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoHolidayRepository : IPublicHolidayRepository
    {
        public Task<IReadOnlyList<PublicHoliday>> GetBetweenAsync(
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PublicHoliday>>([]);

        public Task<IReadOnlyList<PublicHoliday>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsOnDateAsync(DateOnly date, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PublicHoliday?> GetTrackedByHolidayIdAsync(
            Guid holidayId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(PublicHoliday holiday, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoLeaveRepository : IDoctorLeaveRepository
    {
        public Task<IReadOnlyList<DoctorLeave>> GetApprovedForDoctorBetweenAsync(
            Guid doctorId,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoctorLeave>>([]);

        public Task<IReadOnlyList<DoctorLeave>> GetAllForDoctorAsync(
            Guid doctorId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DoctorLeave>> GetPendingAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DoctorLeave?> GetTrackedByLeaveIdAsync(Guid leaveId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(DoctorLeave leave, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoDoctorRepository : IDoctorCacheRepository
    {
        public Task<DoctorCache?> GetActiveAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Blocking never looks the doctor up.");

        public Task<DoctorCache?> GetTrackedByDoctorIdAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DoctorCache>> ListActiveAsync(
            string? specialization,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListSpecializationsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSlotGenerator : ISlotGenerator
    {
        public IReadOnlyList<Slot> Generate(
            DoctorSchedule schedule,
            DateOnly from,
            DateOnly to,
            IReadOnlyCollection<PublicHoliday> holidays,
            IReadOnlyCollection<DoctorLeave> leaves) => [];

        public (DateOnly From, DateOnly To) CurrentHorizon() =>
            (new DateOnly(2026, 9, 25), new DateOnly(2026, 11, 24));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTime now)
        {
            _now = new DateTimeOffset(now);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
