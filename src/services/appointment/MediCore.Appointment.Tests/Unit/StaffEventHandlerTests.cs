using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Services;
using MediCore.Contracts.Events.Staff;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Appointment.Tests.Unit;

public sealed class StaffEventHandlerTests
{
    private static readonly Guid DoctorId = Guid.Parse("3f5b9c1e-1f0a-4a7c-9b3d-7c9a2e4f6b18");
    private static readonly Guid DepartmentId = Guid.Parse("b7d2a9e4-6c31-4f58-a0e2-91c4d7b3f605");
    private static readonly Guid NewDepartmentId = Guid.Parse("0c8e5f1a-2b74-4d93-8a6f-3e1b9d7c2a40");
    private static readonly DateTime Now = new(2026, 9, 22, 6, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime EventTime = new(2026, 9, 22, 6, 0, 0, DateTimeKind.Utc);

    // ── staff.created ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_created_doctor_is_cached_as_bookable()
    {
        var fixture = new Fixture();

        var result = await fixture.Handler.HandleCreatedAsync(Created(role: "Doctor"));

        Assert.IsType<StaffEventAppliedResult>(result);
        var doctor = Assert.Single(fixture.Doctors.Added);
        Assert.Equal(DoctorId, doctor.DoctorId);
        Assert.Equal("Nimal Perera", doctor.FullName);
        Assert.Equal("Cardiology", doctor.Specialization);
        Assert.Equal(DepartmentId, doctor.DepartmentId);
        Assert.True(doctor.IsActive);
        Assert.Equal(EventTime, doctor.LastEventOccurredAtUtc);
        Assert.Equal(StaffEventHandler.IntegrationActor, doctor.CreatedBy);
        Assert.Null(doctor.UpdatedBy);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Created_staff_who_are_not_doctors_are_recorded_but_not_cached()
    {
        var fixture = new Fixture();

        var result = await fixture.Handler.HandleCreatedAsync(Created(role: "Nurse"));

        Assert.Equal(new StaffEventIgnoredResult("NotADoctor"), result);
        Assert.Empty(fixture.Doctors.Added);
        Assert.Equal(ProcessedMessageOutcome.Ignored, Assert.Single(fixture.Messages.Added).Outcome);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task The_doctor_role_is_matched_regardless_of_case()
    {
        var fixture = new Fixture();

        var result = await fixture.Handler.HandleCreatedAsync(Created(role: " doctor "));

        Assert.IsType<StaffEventAppliedResult>(result);
        Assert.Single(fixture.Doctors.Added);
    }

    // ── staff.updated (AC1 and the backfill) ──────────────────────────────────

    [Fact]
    public async Task An_update_refreshes_the_cached_name_and_specialization()
    {
        var existing = CachedDoctor();
        var fixture = new Fixture(existing);

        var result = await fixture.Handler.HandleUpdatedAsync(Updated(
            fullName: "Nimal Perera-Silva",
            specialization: "Neurology",
            departmentId: NewDepartmentId,
            occurredAtUtc: EventTime.AddMinutes(5)));

        Assert.IsType<StaffEventAppliedResult>(result);
        Assert.Empty(fixture.Doctors.Added);
        Assert.Equal("Nimal Perera-Silva", existing.FullName);
        Assert.Equal("Neurology", existing.Specialization);
        Assert.Equal(NewDepartmentId, existing.DepartmentId);
        Assert.True(existing.IsActive);
        Assert.Equal(EventTime.AddMinutes(5), existing.LastEventOccurredAtUtc);
        Assert.Equal(StaffEventHandler.IntegrationActor, existing.UpdatedBy);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_update_for_an_uncached_doctor_inserts_them_which_is_how_the_backfill_works()
    {
        var fixture = new Fixture();

        var result = await fixture.Handler.HandleUpdatedAsync(Updated());

        Assert.IsType<StaffEventAppliedResult>(result);
        var doctor = Assert.Single(fixture.Doctors.Added);
        Assert.Equal(DoctorId, doctor.DoctorId);
        Assert.True(doctor.IsActive);
    }

    [Fact]
    public async Task An_update_saying_the_doctor_is_inactive_makes_them_unbookable()
    {
        var existing = CachedDoctor();
        var fixture = new Fixture(existing);

        await fixture.Handler.HandleUpdatedAsync(Updated(isActive: false, occurredAtUtc: EventTime.AddMinutes(1)));

        Assert.False(existing.IsActive);
    }

    [Fact]
    public async Task A_backfilled_inactive_doctor_is_cached_as_unbookable()
    {
        var fixture = new Fixture();

        await fixture.Handler.HandleUpdatedAsync(Updated(isActive: false));

        Assert.False(Assert.Single(fixture.Doctors.Added).IsActive);
    }

    [Fact]
    public async Task Losing_the_doctor_role_makes_a_cached_doctor_unbookable_but_keeps_the_row()
    {
        var existing = CachedDoctor();
        var fixture = new Fixture(existing);

        var result = await fixture.Handler.HandleUpdatedAsync(Updated(
            role: "Nurse",
            fullName: "Renamed As Nurse",
            occurredAtUtc: EventTime.AddMinutes(1)));

        Assert.IsType<StaffEventAppliedResult>(result);
        Assert.False(existing.IsActive);
        Assert.Equal("Nimal Perera", existing.FullName);
        Assert.Equal(EventTime.AddMinutes(1), existing.LastEventOccurredAtUtc);
    }

    [Fact]
    public async Task An_update_about_someone_who_was_never_a_doctor_is_ignored()
    {
        var fixture = new Fixture();

        var result = await fixture.Handler.HandleUpdatedAsync(Updated(role: "Receptionist"));

        Assert.Equal(new StaffEventIgnoredResult("NotADoctor"), result);
        Assert.Empty(fixture.Doctors.Added);
    }

    [Fact]
    public async Task A_legacy_update_without_a_role_refreshes_details_but_leaves_bookability_alone()
    {
        var existing = CachedDoctor(isActive: false);
        var fixture = new Fixture(existing);

        var result = await fixture.Handler.HandleUpdatedAsync(Updated(
            role: null,
            isActive: null,
            specialization: "Neurology",
            occurredAtUtc: EventTime.AddMinutes(1)));

        Assert.IsType<StaffEventAppliedResult>(result);
        Assert.Equal("Neurology", existing.Specialization);
        Assert.False(existing.IsActive);
    }

    [Fact]
    public async Task A_legacy_update_for_an_uncached_person_is_ignored_because_it_cannot_say_they_are_a_doctor()
    {
        var fixture = new Fixture();

        var result = await fixture.Handler.HandleUpdatedAsync(Updated(role: null, isActive: null));

        Assert.Equal(new StaffEventIgnoredResult("UnknownDoctor"), result);
        Assert.Empty(fixture.Doctors.Added);
        Assert.Equal(ProcessedMessageOutcome.Ignored, Assert.Single(fixture.Messages.Added).Outcome);
    }

    // ── staff.deactivated (AC2) ───────────────────────────────────────────────

    [Fact]
    public async Task Deactivation_removes_the_doctor_from_bookable_doctors()
    {
        var existing = CachedDoctor();
        var fixture = new Fixture(existing);

        var result = await fixture.Handler.HandleDeactivatedAsync(Deactivated(EventTime.AddMinutes(1)));

        Assert.IsType<StaffEventAppliedResult>(result);
        Assert.False(existing.IsActive);
        Assert.False(existing.IsDeleted);
        Assert.Equal(EventTime.AddMinutes(1), existing.LastEventOccurredAtUtc);
        Assert.Equal(ProcessedMessageOutcome.Applied, Assert.Single(fixture.Messages.Added).Outcome);
    }

    [Fact]
    public async Task Deactivating_someone_not_in_the_cache_is_ignored()
    {
        var fixture = new Fixture();

        var result = await fixture.Handler.HandleDeactivatedAsync(Deactivated(EventTime));

        Assert.Equal(new StaffEventIgnoredResult("UnknownDoctor"), result);
        Assert.Empty(fixture.Doctors.Added);
    }

    // ── Idempotency and ordering ──────────────────────────────────────────────

    [Fact]
    public async Task A_message_already_processed_is_skipped_without_writing_anything()
    {
        var existing = CachedDoctor();
        var fixture = new Fixture(existing);
        var updated = Updated(fullName: "Should Not Apply", occurredAtUtc: EventTime.AddMinutes(1));
        fixture.Messages.Existing.Add(updated.MessageId);

        var result = await fixture.Handler.HandleUpdatedAsync(updated);

        Assert.IsType<StaffEventDuplicateResult>(result);
        Assert.Equal("Nimal Perera", existing.FullName);
        Assert.Empty(fixture.Messages.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Losing_the_race_to_record_a_message_counts_as_a_duplicate()
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.ThrowDuplicate = true;

        var result = await fixture.Handler.HandleCreatedAsync(Created(role: "Doctor"));

        Assert.IsType<StaffEventDuplicateResult>(result);
    }

    [Fact]
    public async Task An_event_older_than_the_cached_state_is_recorded_as_stale_and_not_applied()
    {
        var existing = CachedDoctor();
        var fixture = new Fixture(existing);

        var result = await fixture.Handler.HandleUpdatedAsync(Updated(
            fullName: "Older Name",
            occurredAtUtc: EventTime.AddMinutes(-1)));

        Assert.IsType<StaffEventStaleResult>(result);
        Assert.Equal("Nimal Perera", existing.FullName);
        Assert.Equal(EventTime, existing.LastEventOccurredAtUtc);
        Assert.Equal(ProcessedMessageOutcome.Stale, Assert.Single(fixture.Messages.Added).Outcome);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_stale_deactivation_does_not_deactivate_a_doctor_reactivated_since()
    {
        var existing = CachedDoctor();
        var fixture = new Fixture(existing);

        var result = await fixture.Handler.HandleDeactivatedAsync(Deactivated(EventTime.AddMinutes(-1)));

        Assert.IsType<StaffEventStaleResult>(result);
        Assert.True(existing.IsActive);
    }

    [Fact]
    public async Task An_event_with_the_same_timestamp_as_the_cache_is_applied()
    {
        var existing = CachedDoctor();
        var fixture = new Fixture(existing);

        var result = await fixture.Handler.HandleUpdatedAsync(Updated(specialization: "Neurology"));

        Assert.IsType<StaffEventAppliedResult>(result);
        Assert.Equal("Neurology", existing.Specialization);
    }

    [Fact]
    public async Task The_processed_message_row_records_where_the_message_came_from()
    {
        var fixture = new Fixture();
        var created = Created(role: "Doctor");

        await fixture.Handler.HandleCreatedAsync(created);

        var processed = Assert.Single(fixture.Messages.Added);
        Assert.Equal(created.MessageId, processed.MessageId);
        Assert.Equal("staff.created", processed.EventType);
        Assert.Equal("medicore-appointment", processed.ConsumerGroup);
        Assert.Equal("staff-events", processed.SourceTopic);
        Assert.Equal(ProcessedMessageOutcome.Applied, processed.Outcome);
        Assert.Equal(Now, processed.ProcessedAtUtc);
    }

    [Fact]
    public async Task An_unspecified_kind_timestamp_is_stored_as_utc()
    {
        var fixture = new Fixture();
        var unspecified = DateTime.SpecifyKind(EventTime, DateTimeKind.Unspecified);

        await fixture.Handler.HandleUpdatedAsync(Updated(occurredAtUtc: unspecified));

        Assert.Equal(DateTimeKind.Utc, Assert.Single(fixture.Doctors.Added).LastEventOccurredAtUtc.Kind);
    }

    // ── Rejected events ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("MissingStaffId")]
    [InlineData("UnsupportedEventVersion")]
    [InlineData("MissingFullName")]
    [InlineData("FullNameTooLong")]
    [InlineData("SpecializationTooLong")]
    public async Task Invalid_events_are_recorded_as_rejected_and_change_nothing(string reason)
    {
        var fixture = new Fixture();
        var updated = reason switch
        {
            "MissingStaffId" => Updated() with { StaffId = Guid.Empty },
            "UnsupportedEventVersion" => Updated() with { Version = 2 },
            "MissingFullName" => Updated(fullName: "  "),
            "FullNameTooLong" => Updated(fullName: new string('a', 201)),
            "SpecializationTooLong" => Updated(specialization: new string('a', 101)),
            _ => throw new ArgumentOutOfRangeException(nameof(reason))
        };

        var result = await fixture.Handler.HandleUpdatedAsync(updated);

        Assert.Equal(new StaffEventRejectedResult(reason), result);
        Assert.Empty(fixture.Doctors.Added);
        Assert.Equal(ProcessedMessageOutcome.Rejected, Assert.Single(fixture.Messages.Added).Outcome);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_event_without_a_message_id_is_rejected_without_being_recorded()
    {
        var fixture = new Fixture();

        var result = await fixture.Handler.HandleUpdatedAsync(Updated() with { MessageId = Guid.Empty });

        Assert.Equal(new StaffEventRejectedResult("MissingMessageId"), result);
        Assert.Empty(fixture.Messages.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static StaffCreatedEvent Created(string role) => new()
    {
        StaffId = DoctorId,
        UserId = Guid.NewGuid(),
        FullName = "Nimal Perera",
        Email = "nimal@medicore.test",
        Role = role,
        Specialization = "Cardiology",
        DepartmentId = DepartmentId,
        OccurredAtUtc = EventTime
    };

    private static StaffUpdatedEvent Updated(
        string? role = "Doctor",
        bool? isActive = true,
        string fullName = "Nimal Perera",
        string specialization = "Cardiology",
        Guid? departmentId = null,
        DateTime? occurredAtUtc = null) => new()
        {
            StaffId = DoctorId,
            FullName = fullName,
            Specialization = specialization,
            DepartmentId = departmentId ?? DepartmentId,
            Role = role,
            IsActive = isActive,
            OccurredAtUtc = occurredAtUtc ?? EventTime
        };

    private static StaffDeactivatedEvent Deactivated(DateTime occurredAtUtc) => new()
    {
        StaffId = DoctorId,
        OccurredAtUtc = occurredAtUtc
    };

    private static DoctorCache CachedDoctor(bool isActive = true) => new()
    {
        DoctorId = DoctorId,
        FullName = "Nimal Perera",
        Specialization = "Cardiology",
        DepartmentId = DepartmentId,
        IsActive = isActive,
        LastEventOccurredAtUtc = EventTime,
        CreatedBy = StaffEventHandler.IntegrationActor
    };

    private sealed class Fixture
    {
        public Fixture(DoctorCache? existing = null)
        {
            Doctors = new FakeDoctorCacheRepository(existing);
            Messages = new FakeProcessedMessageRepository();
            UnitOfWork = new FakeUnitOfWork();
            Handler = new StaffEventHandler(
                Doctors,
                Messages,
                UnitOfWork,
                new FixedTimeProvider(Now),
                NullLogger<StaffEventHandler>.Instance);
        }

        public FakeDoctorCacheRepository Doctors { get; }
        public FakeProcessedMessageRepository Messages { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public StaffEventHandler Handler { get; }
    }

    private sealed class FakeDoctorCacheRepository : IDoctorCacheRepository
    {
        private readonly List<DoctorCache> _rows = [];

        public FakeDoctorCacheRepository(DoctorCache? existing)
        {
            if (existing is not null) _rows.Add(existing);
        }

        public List<DoctorCache> Added { get; } = [];

        public Task<DoctorCache?> GetTrackedByDoctorIdAsync(
            Guid doctorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.FirstOrDefault(doctor => doctor.DoctorId == doctorId));

        public Task<DoctorCache?> GetActiveAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The handler never reads bookable doctors.");

        public Task<IReadOnlyList<DoctorCache>> ListActiveAsync(
            string? specialization,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The handler never lists doctors.");

        public Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default)
        {
            Added.Add(doctor);
            _rows.Add(doctor);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProcessedMessageRepository : IProcessedMessageRepository
    {
        public HashSet<Guid> Existing { get; } = [];
        public List<ProcessedMessage> Added { get; } = [];

        public Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Existing.Contains(messageId));

        public Task AddAsync(ProcessedMessage message, CancellationToken cancellationToken = default)
        {
            Added.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public bool ThrowDuplicate { get; set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (ThrowDuplicate) throw new DuplicateProcessedMessageException();
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
