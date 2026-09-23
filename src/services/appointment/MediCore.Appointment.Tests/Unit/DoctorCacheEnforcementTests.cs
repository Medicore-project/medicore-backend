using System.Security.Claims;
using MediCore.Appointment.Api.Controllers;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using MediCore.Appointment.Application.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>
/// SCRUM-33: schedules and available slots are only for doctors the local doctor cache holds as
/// bookable. Real services and controllers over fakes, so each test covers the rule and the 404 it
/// becomes.
/// </summary>
public sealed class DoctorCacheEnforcementTests
{
    private static readonly Guid BookableDoctorId = Guid.Parse("3f5b9c1e-1f0a-4a7c-9b3d-7c9a2e4f6b18");
    private static readonly Guid DeactivatedDoctorId = Guid.Parse("a1c4e8d2-55b6-4f39-8e71-2d6c0b9a4371");
    private static readonly Guid UnknownDoctorId = Guid.Parse("5e9d3a72-8c14-4b06-9f2e-6a1d0c7b8e35");
    private static readonly DateTime Now = new(2026, 9, 22, 6, 30, 0, DateTimeKind.Utc);

    // ── Creating a schedule ───────────────────────────────────────────────────

    [Fact]
    public async Task A_schedule_is_created_for_a_bookable_doctor()
    {
        var fixture = new Fixture();

        var result = await fixture.Schedules.CreateAsync(ScheduleRequest(BookableDoctorId), "admin@medicore.lk");

        Assert.IsType<ScheduleCreatedResult>(result);
        Assert.Single(fixture.ScheduleRepository.Added);
        Assert.Equal(BookableDoctorId, Assert.Single(fixture.Revisions.Reconciled));
    }

    [Theory]
    [MemberData(nameof(UnbookableDoctors))]
    public async Task A_schedule_for_an_unbookable_doctor_is_refused_before_anything_is_written(Guid doctorId)
    {
        var fixture = new Fixture();

        var result = await fixture.Schedules.CreateAsync(ScheduleRequest(doctorId), "admin@medicore.lk");

        Assert.IsType<ScheduleCreateDoctorNotFoundResult>(result);
        Assert.Empty(fixture.ScheduleRepository.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.Revisions.Reconciled);
    }

    [Fact]
    public async Task The_doctor_is_checked_before_overlap_so_an_unknown_doctor_never_reads_as_a_conflict()
    {
        var fixture = new Fixture();
        fixture.ScheduleRepository.ForOverlap = [ExistingSchedule(UnknownDoctorId)];

        var result = await fixture.Schedules.CreateAsync(ScheduleRequest(UnknownDoctorId), "admin@medicore.lk");

        Assert.IsType<ScheduleCreateDoctorNotFoundResult>(result);
    }

    [Fact]
    public async Task Creating_a_schedule_for_an_unbookable_doctor_returns_404()
    {
        var fixture = new Fixture();

        var result = await fixture.SchedulesController.Create(
            ScheduleRequest(DeactivatedDoctorId),
            CancellationToken.None);

        AssertDoctorNotFound(result);
    }

    // ── Available slots (AC2) ─────────────────────────────────────────────────

    [Fact]
    public async Task A_bookable_doctors_free_slots_are_returned()
    {
        var fixture = new Fixture();
        fixture.SlotRepository.Available = [FreeSlot(BookableDoctorId)];

        var result = await fixture.Slots.GetAvailableAsync(BookableDoctorId, null, null);

        var found = Assert.IsType<AvailableSlotsFoundResult>(result);
        Assert.Equal(BookableDoctorId, Assert.Single(found.Slots).DoctorId);
    }

    [Theory]
    [MemberData(nameof(UnbookableDoctors))]
    public async Task An_unbookable_doctor_offers_no_slots_even_if_old_slot_rows_remain(Guid doctorId)
    {
        var fixture = new Fixture();
        fixture.SlotRepository.Available = [FreeSlot(doctorId)];

        var result = await fixture.Slots.GetAvailableAsync(doctorId, null, null);

        Assert.IsType<AvailableSlotsDoctorNotFoundResult>(result);
        Assert.Equal(0, fixture.SlotRepository.AvailableQueries);
    }

    [Fact]
    public async Task Available_slots_for_a_deactivated_doctor_return_404()
    {
        var fixture = new Fixture();

        var result = await fixture.SlotsController.GetAvailable(
            DeactivatedDoctorId,
            null,
            null,
            CancellationToken.None);

        AssertDoctorNotFound(result);
    }

    [Fact]
    public async Task Available_slots_for_a_bookable_doctor_return_200()
    {
        var fixture = new Fixture();
        fixture.SlotRepository.Available = [FreeSlot(BookableDoctorId)];

        var result = await fixture.SlotsController.GetAvailable(
            BookableDoctorId,
            null,
            null,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<SlotResponse>>(ok.Value));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static TheoryData<Guid> UnbookableDoctors => new() { DeactivatedDoctorId, UnknownDoctorId };

    private static void AssertDoctorNotFound(IActionResult result)
    {
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Equal("Doctor not found or not bookable.", problem.Title);
    }

    private static CreateDoctorScheduleRequest ScheduleRequest(Guid doctorId) => new(
        doctorId,
        DayOfWeek.Monday,
        new TimeOnly(9, 0),
        new TimeOnly(12, 0),
        15,
        new DateOnly(2026, 9, 28),
        null);

    private static DoctorSchedule ExistingSchedule(Guid doctorId) => new()
    {
        DoctorId = doctorId,
        DayOfWeek = DayOfWeek.Monday,
        StartTime = new TimeOnly(8, 0),
        EndTime = new TimeOnly(13, 0),
        SlotDurationMinutes = 15,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        IsActive = true
    };

    private static Slot FreeSlot(Guid doctorId) => new()
    {
        DoctorId = doctorId,
        StartUtc = new DateTime(2026, 9, 28, 3, 30, 0, DateTimeKind.Utc),
        EndUtc = new DateTime(2026, 9, 28, 3, 45, 0, DateTimeKind.Utc),
        SlotDate = new DateOnly(2026, 9, 28),
        DurationMinutes = 15,
        Status = SlotStatus.Available
    };

    private sealed class Fixture
    {
        public Fixture()
        {
            var doctors = new FakeDoctorCacheRepository(
                new DoctorCache { DoctorId = BookableDoctorId, FullName = "Nimal Perera", IsActive = true },
                new DoctorCache { DoctorId = DeactivatedDoctorId, FullName = "Kamala Silva", IsActive = false });

            Schedules = new DoctorScheduleService(
                ScheduleRepository,
                doctors,
                new ScheduleOverlapDetector(),
                Revisions,
                UnitOfWork);
            Slots = new SlotService(
                SlotRepository,
                doctors,
                new FakeSlotGenerator(),
                UnitOfWork,
                new FixedTimeProvider(Now));

            var context = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.Email, "admin@medicore.lk")],
                        "Test"))
                }
            };
            SchedulesController = new SchedulesController(
                new CreateDoctorScheduleRequestValidator(),
                new UpdateDoctorScheduleRequestValidator(),
                Schedules)
            {
                ControllerContext = context
            };
            SlotsController = new SlotsController(new BlockSlotRequestValidator(), Slots)
            {
                ControllerContext = context
            };
        }

        public FakeDoctorScheduleRepository ScheduleRepository { get; } = new();
        public FakeSlotRepository SlotRepository { get; } = new();
        public FakeScheduleRevisionService Revisions { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public DoctorScheduleService Schedules { get; }
        public SlotService Slots { get; }
        public SchedulesController SchedulesController { get; }
        public SlotsController SlotsController { get; }
    }

    private sealed class FakeDoctorCacheRepository : IDoctorCacheRepository
    {
        private readonly List<DoctorCache> _doctors;

        public FakeDoctorCacheRepository(params DoctorCache[] doctors)
        {
            _doctors = [.. doctors];
        }

        public Task<DoctorCache?> GetActiveAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_doctors.FirstOrDefault(doctor => doctor.DoctorId == doctorId && doctor.IsActive));

        public Task<DoctorCache?> GetTrackedByDoctorIdAsync(
            Guid doctorId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Scheduling never writes to the doctor cache.");

        public Task<IReadOnlyList<DoctorCache>> ListActiveAsync(
            string? specialization,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Scheduling never lists doctors.");

        public Task<IReadOnlyList<string>> ListSpecializationsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only the public booking page lists specializations.");

        public Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Scheduling never writes to the doctor cache.");
    }

    private sealed class FakeDoctorScheduleRepository : IDoctorScheduleRepository
    {
        public List<DoctorSchedule> ForOverlap { get; set; } = [];
        public List<DoctorSchedule> Added { get; } = [];

        public Task<IReadOnlyList<DoctorSchedule>> GetForOverlapCheckAsync(
            Guid doctorId,
            DayOfWeek dayOfWeek,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoctorSchedule>>(ForOverlap);

        public Task AddAsync(DoctorSchedule schedule, CancellationToken cancellationToken = default)
        {
            Added.Add(schedule);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DoctorSchedule>> GetActiveForDoctorAsync(
            Guid doctorId,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoctorSchedule>>([]);

        public Task<IReadOnlyList<Guid>> GetDoctorIdsWithActiveSchedulesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyList<DoctorSchedule>> GetAllForDoctorAsync(
            Guid doctorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoctorSchedule>>([]);

        public Task<DoctorSchedule?> GetByScheduleIdAsync(
            Guid scheduleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DoctorSchedule?>(null);

        public Task<DoctorSchedule?> GetTrackedByScheduleIdAsync(
            Guid scheduleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DoctorSchedule?>(null);
    }

    private sealed class FakeSlotRepository : ISlotRepository
    {
        public List<Slot> Available { get; set; } = [];
        public int AvailableQueries { get; private set; }

        public Task<IReadOnlyList<Slot>> GetAvailableAsync(
            Guid doctorId,
            DateOnly from,
            DateOnly to,
            DateTime nowUtc,
            CancellationToken cancellationToken = default)
        {
            AvailableQueries++;
            return Task.FromResult<IReadOnlyList<Slot>>(
                [.. Available.Where(slot => slot.DoctorId == doctorId)]);
        }

        public Task<IReadOnlyList<Slot>> GetTrackedForDoctorBetweenAsync(
            Guid doctorId,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Slot>>([]);

        public Task AddRangeAsync(IReadOnlyCollection<Slot> slots, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void RemoveRange(IReadOnlyCollection<Slot> slots) { }

        public Task<IReadOnlyList<Slot>> GetFlaggedAsync(
            Guid? doctorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Slot>>([]);

        public Task<Slot?> GetTrackedBySlotIdAsync(
            Guid slotId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Slot?>(null);
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
            (new DateOnly(2026, 9, 22), new DateOnly(2026, 11, 21));
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
            return Task.FromResult(SlotReconciliationSummary.Empty);
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
