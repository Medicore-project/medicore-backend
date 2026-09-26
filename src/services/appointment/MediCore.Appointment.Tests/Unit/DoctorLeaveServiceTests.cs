using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Services;

namespace MediCore.Appointment.Tests.Unit;

public sealed class DoctorLeaveServiceTests
{
    private static readonly Guid DoctorId = Guid.Parse("3f5b9c1e-1f0a-4a7c-9b3d-7c9a2e4f6b18");
    private static readonly Guid OtherDoctorId = Guid.Parse("a1c4e8d2-55b6-4f39-8e71-2d6c0b9a4371");
    private static readonly DateTime Now = new(2026, 9, 21, 6, 30, 0, DateTimeKind.Utc);

    // ── Approved leave in a range ─────────────────────────────────────────────

    [Fact]
    public async Task Approved_leave_is_read_for_the_requested_doctor_and_range()
    {
        var fixture = new Fixture();
        var from = new DateOnly(2026, 9, 21);
        var to = new DateOnly(2026, 9, 27);

        await fixture.Service.GetApprovedBetweenAsync(DoctorId, from, to);

        Assert.Equal(DoctorId, fixture.Leaves.LastApprovedQuery?.DoctorId);
        Assert.Equal(from, fixture.Leaves.LastApprovedQuery?.From);
        Assert.Equal(to, fixture.Leaves.LastApprovedQuery?.To);
    }

    [Fact]
    public async Task Approved_leave_is_returned_with_the_dates_a_booking_grid_needs()
    {
        var leave = Leave(LeaveStatus.Approved, reason: "Conference");
        var fixture = new Fixture();
        fixture.Leaves.Approved = [leave];

        var result = await fixture.Service.GetApprovedBetweenAsync(
            DoctorId,
            new DateOnly(2026, 9, 21),
            new DateOnly(2026, 9, 27));

        var response = Assert.Single(result);
        Assert.Equal(leave.LeaveId, response.LeaveId);
        Assert.Equal(leave.StartDate, response.StartDate);
        Assert.Equal(leave.EndDate, response.EndDate);
        Assert.Equal("Conference", response.Reason);
        Assert.Equal(LeaveStatus.Approved, response.Status);
    }

    [Fact]
    public async Task A_doctor_with_no_approved_leave_yields_an_empty_list_not_an_error()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.GetApprovedBetweenAsync(
            DoctorId,
            new DateOnly(2026, 9, 21),
            new DateOnly(2026, 9, 27));

        Assert.Empty(result);
    }

    // ── Withdrawal ownership ──────────────────────────────────────────────────

    [Fact]
    public async Task Withdrawing_an_unknown_request_reports_not_found()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.WithdrawAsync(Guid.NewGuid(), "doctor@medicore.lk", DoctorId);

        Assert.IsType<LeaveWithdrawNotFoundResult>(result);
    }

    [Fact]
    public async Task A_doctor_cannot_withdraw_another_doctors_leave()
    {
        var leave = Leave(LeaveStatus.Approved);
        var fixture = new Fixture(leave);

        var result = await fixture.Service.WithdrawAsync(leave.LeaveId, "other@medicore.lk", OtherDoctorId);

        Assert.IsType<LeaveWithdrawForbiddenResult>(result);
    }

    [Fact]
    public async Task A_caller_with_no_staff_id_cannot_withdraw_leave()
    {
        // A missing staffId claim must read as "owns nothing", never as a wildcard.
        var leave = Leave(LeaveStatus.Approved);
        var fixture = new Fixture(leave);

        var result = await fixture.Service.WithdrawAsync(leave.LeaveId, "admin@medicore.lk", null);

        Assert.IsType<LeaveWithdrawForbiddenResult>(result);
    }

    [Fact]
    public async Task A_refused_withdrawal_leaves_the_request_and_the_calendar_untouched()
    {
        var leave = Leave(LeaveStatus.Approved);
        var fixture = new Fixture(leave);

        await fixture.Service.WithdrawAsync(leave.LeaveId, "other@medicore.lk", OtherDoctorId);

        Assert.False(leave.IsDeleted);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.Revisions.Reconciled);
    }

    [Fact]
    public async Task A_doctor_withdraws_their_own_request()
    {
        var leave = Leave(LeaveStatus.Approved);
        var fixture = new Fixture(leave);

        var result = await fixture.Service.WithdrawAsync(leave.LeaveId, "doctor@medicore.lk", DoctorId);

        Assert.IsType<LeaveWithdrawnResult>(result);
        Assert.True(leave.IsDeleted);
        Assert.Equal("doctor@medicore.lk", leave.UpdatedBy);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Withdrawing_approved_leave_rebuilds_that_doctors_slots()
    {
        var leave = Leave(LeaveStatus.Approved);
        var fixture = new Fixture(leave);

        await fixture.Service.WithdrawAsync(leave.LeaveId, "doctor@medicore.lk", DoctorId);

        Assert.Equal(DoctorId, Assert.Single(fixture.Revisions.Reconciled));
    }

    [Fact]
    public async Task Withdrawing_pending_leave_touches_no_slots()
    {
        // A pending request never suppressed anything, so there is nothing to rebuild.
        var leave = Leave(LeaveStatus.Pending);
        var fixture = new Fixture(leave);

        var result = await fixture.Service.WithdrawAsync(leave.LeaveId, "doctor@medicore.lk", DoctorId);

        Assert.IsType<LeaveWithdrawnResult>(result);
        Assert.True(leave.IsDeleted);
        Assert.Empty(fixture.Revisions.Reconciled);
    }

    // ── Submitting leave against the doctor cache (SCRUM-33) ──────────────────

    [Fact]
    public async Task A_bookable_doctor_submits_a_pending_request()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateAsync(CreateRequest(DoctorId), "doctor@medicore.lk");

        var created = Assert.IsType<LeaveCreatedResult>(result);
        Assert.Equal(LeaveStatus.Pending, created.Leave.Status);
        Assert.Single(fixture.Leaves.Added);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Leave_for_a_doctor_missing_from_the_cache_is_refused_and_nothing_is_saved()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CreateAsync(CreateRequest(OtherDoctorId), "other@medicore.lk");

        Assert.IsType<LeaveCreateDoctorNotFoundResult>(result);
        Assert.Empty(fixture.Leaves.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Leave_for_a_deactivated_doctor_is_refused()
    {
        var fixture = new Fixture();
        fixture.Doctors.Doctors[0].IsActive = false;

        var result = await fixture.Service.CreateAsync(CreateRequest(DoctorId), "doctor@medicore.lk");

        Assert.IsType<LeaveCreateDoctorNotFoundResult>(result);
        Assert.Empty(fixture.Leaves.Added);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CreateDoctorLeaveRequest CreateRequest(Guid doctorId) => new(
        doctorId,
        new DateOnly(2026, 9, 28),
        new DateOnly(2026, 9, 30),
        "Conference");

    private static DoctorLeave Leave(string status, string? reason = null) => new()
    {
        LeaveId = Guid.Parse("7c2f4b10-9d83-4e56-8a21-0f5b6c3d7e94"),
        DoctorId = DoctorId,
        StartDate = new DateOnly(2026, 9, 22),
        EndDate = new DateOnly(2026, 9, 24),
        Reason = reason,
        Status = status,
        CreatedBy = "doctor@medicore.lk"
    };

    private sealed class Fixture
    {
        public Fixture(DoctorLeave? leave = null)
        {
            Leaves = new FakeDoctorLeaveRepository(leave);

            // DoctorId is cached and bookable; OtherDoctorId is not in the cache at all.
            Doctors = new FakeDoctorCacheRepository(
                new DoctorCache { DoctorId = DoctorId, FullName = "Nimal Perera", IsActive = true });
            Revisions = new FakeScheduleRevisionService();
            UnitOfWork = new FakeUnitOfWork();
            Service = new DoctorLeaveService(
                Leaves,
                Doctors,
                Revisions,
                UnitOfWork,
                new FixedTimeProvider(Now));
        }

        public FakeDoctorLeaveRepository Leaves { get; }
        public FakeDoctorCacheRepository Doctors { get; }
        public FakeScheduleRevisionService Revisions { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public DoctorLeaveService Service { get; }
    }

    private sealed record ApprovedQuery(Guid DoctorId, DateOnly From, DateOnly To);

    private sealed class FakeDoctorCacheRepository : IDoctorCacheRepository
    {
        public FakeDoctorCacheRepository(params DoctorCache[] doctors)
        {
            Doctors = [.. doctors];
        }

        public List<DoctorCache> Doctors { get; }

        public Task<DoctorCache?> GetActiveAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Doctors.FirstOrDefault(doctor => doctor.DoctorId == doctorId && doctor.IsActive));

        public Task<DoctorCache?> GetTrackedByDoctorIdAsync(
            Guid doctorId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Leave never writes to the doctor cache.");

        public Task<IReadOnlyList<DoctorCache>> ListActiveAsync(
            string? specialization,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Leave never lists doctors.");

        public Task<IReadOnlyList<string>> ListSpecializationsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only the public booking page lists specializations.");

        public Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Leave never writes to the doctor cache.");
    }

    private sealed class FakeDoctorLeaveRepository : IDoctorLeaveRepository
    {
        private readonly DoctorLeave? _leave;

        public FakeDoctorLeaveRepository(DoctorLeave? leave)
        {
            _leave = leave;
        }

        public List<DoctorLeave> Approved { get; set; } = [];
        public List<DoctorLeave> Added { get; } = [];
        public ApprovedQuery? LastApprovedQuery { get; private set; }

        public Task<IReadOnlyList<DoctorLeave>> GetApprovedForDoctorBetweenAsync(
            Guid doctorId,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default)
        {
            LastApprovedQuery = new ApprovedQuery(doctorId, from, to);
            return Task.FromResult<IReadOnlyList<DoctorLeave>>(Approved);
        }

        public Task<IReadOnlyList<DoctorLeave>> GetAllForDoctorAsync(
            Guid doctorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoctorLeave>>(_leave is null ? [] : [_leave]);

        public Task<IReadOnlyList<DoctorLeave>> GetPendingAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoctorLeave>>([]);

        public Task<DoctorLeave?> GetTrackedByLeaveIdAsync(
            Guid leaveId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_leave is not null && _leave.LeaveId == leaveId ? _leave : null);

        public Task AddAsync(DoctorLeave leave, CancellationToken cancellationToken = default)
        {
            Added.Add(leave);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScheduleRevisionService : IScheduleRevisionService
    {
        public List<Guid> Reconciled { get; } = [];

        public Task<SlotReconciliationSummary> ReconcileDoctorAsync(
            Guid doctorId,
            string flagReason,
            CancellationToken cancellationToken = default)
        {
            Reconciled.Add(doctorId);
            return Task.FromResult(new SlotReconciliationSummary(1, 4, 0, 0));
        }

        public Task<SlotReconciliationSummary> ReconcileAllDoctorsAsync(
            string flagReason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SlotReconciliationSummary.Empty);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        // These services never open an explicit transaction; running the work directly keeps the
        // fake honest about that without pretending to roll anything back.
        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> work,
            CancellationToken cancellationToken = default) =>
            work(cancellationToken);
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
