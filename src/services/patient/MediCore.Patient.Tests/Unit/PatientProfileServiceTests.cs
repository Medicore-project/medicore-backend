using System.Text.Json;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Application.Services;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Tests.Unit;

public sealed class PatientProfileServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 8, 30, 0, TimeSpan.Zero);
    private static readonly PatientAccessContext Access = new(
        "receptionist-1", "Receptionist", "corr-24", "127.0.0.1");

    [Fact]
    public async Task Get_existing_patient_writes_view_audit_before_returning_profile()
    {
        var patient = ExistingPatient();
        var fixture = new Fixture(patient);

        var result = await fixture.Service.GetByIdAsync(patient.Id, Access);

        Assert.NotNull(result);
        Assert.Equal(patient.PatientNumber, result.PatientNumber);
        var audit = Assert.Single(fixture.Audits.Added);
        Assert.Equal("PatientViewed", audit.Action);
        Assert.Equal(patient.Id, audit.PatientId);
        Assert.Equal("receptionist-1", audit.ActorId);
        Assert.Equal("corr-24", audit.CorrelationId);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Get_missing_patient_does_not_write_false_view_audit()
    {
        var fixture = new Fixture(null);

        var result = await fixture.Service.GetByIdAsync(Guid.NewGuid(), Access);

        Assert.Null(result);
        Assert.Empty(fixture.Audits.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Update_changes_allowed_fields_and_creates_patient_updated_event()
    {
        var patient = ExistingPatient();
        var originalNic = patient.Nic;
        var originalPatientNumber = patient.PatientNumber;
        var fixture = new Fixture(patient);

        var result = await fixture.Service.UpdateAsync(patient.Id, UpdateRequest(), Access);

        var updated = Assert.IsType<PatientUpdatedResult>(result);
        Assert.Equal("Updated Name", updated.Patient.FullName);
        Assert.Equal("updated@example.com", patient.Email);
        Assert.Equal("0771234567", patient.Phone);
        Assert.Equal(originalNic, patient.Nic);
        Assert.Equal(originalPatientNumber, patient.PatientNumber);
        Assert.Equal("receptionist-1", patient.UpdatedBy);

        var outbox = Assert.Single(fixture.Outbox.Added);
        Assert.Equal("patient.updated", outbox.EventType);
        Assert.Equal(patient.Id.ToString(), outbox.EventKey);
        Assert.Equal("corr-24", outbox.CorrelationId);

        using var payload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal(patient.Id, payload.RootElement.GetProperty("patientId").GetGuid());
        Assert.Equal("Updated Name", payload.RootElement.GetProperty("fullName").GetString());

        Assert.Equal("PatientUpdated", Assert.Single(fixture.Audits.Added).Action);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Update_missing_patient_returns_not_found_without_writes()
    {
        var fixture = new Fixture(null);

        var result = await fixture.Service.UpdateAsync(Guid.NewGuid(), UpdateRequest(), Access);

        Assert.IsType<PatientUpdateNotFoundResult>(result);
        Assert.Empty(fixture.Outbox.Added);
        Assert.Empty(fixture.Audits.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Delete_marks_patient_as_deleted_instead_of_removing_it()
    {
        var patient = ExistingPatient();
        var fixture = new Fixture(patient);

        var result = await fixture.Service.DeleteAsync(patient.Id, Access);

        Assert.True(result);
        Assert.True(patient.IsDeleted);
        Assert.Equal("receptionist-1", patient.UpdatedBy);
        Assert.Equal("PatientDeleted", Assert.Single(fixture.Audits.Added).Action);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Delete_missing_patient_returns_false_without_writes()
    {
        var fixture = new Fixture(null);

        var result = await fixture.Service.DeleteAsync(Guid.NewGuid(), Access);

        Assert.False(result);
        Assert.Empty(fixture.Audits.Added);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    private static PatientEntity ExistingPatient() => new()
    {
        Id = Guid.NewGuid(),
        PatientNumber = "PAT-000024",
        Nic = "200012345678",
        FirstName = "Original",
        LastName = "Patient",
        DateOfBirth = new DateOnly(2000, 5, 15),
        Gender = "Female",
        Email = "original@example.com",
        Phone = "0711234567",
        AddressLine1 = "12 Old Road",
        District = "Colombo",
        CreatedAt = Now.AddDays(-1).UtcDateTime,
        CreatedBy = "receptionist-0"
    };

    private static UpdatePatientRequest UpdateRequest() => new(
        "Updated",
        "Name",
        new DateOnly(2000, 5, 15),
        "Female",
        "UPDATED@example.com",
        "077 123 4567",
        "14 New Road",
        "Apartment 2",
        "Gampaha",
        "Emergency Person",
        "071 987 6543");

    private sealed class Fixture
    {
        public Fixture(PatientEntity? patient)
        {
            Patients = new FakePatientRepository(patient);
            Audits = new FakeAuditRepository();
            Outbox = new FakeOutboxRepository();
            UnitOfWork = new FakeUnitOfWork();
            Service = new PatientProfileService(
                Patients,
                Audits,
                Outbox,
                UnitOfWork,
                new FixedTimeProvider(Now));
        }

        public FakePatientRepository Patients { get; }
        public FakeAuditRepository Audits { get; }
        public FakeOutboxRepository Outbox { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public PatientProfileService Service { get; }
    }

    private sealed class FakePatientRepository : IPatientRepository
    {
        private readonly PatientEntity? _patient;

        public FakePatientRepository(PatientEntity? patient)
        {
            _patient = patient;
        }

        public Task<PatientEntity?> FindByNicAsync(
            string normalizedNic,
            bool includeArchived,
            CancellationToken cancellationToken = default) => Task.FromResult(_patient);

        public Task<PatientEntity?> FindByPatientNumberAndDateOfBirthAsync(
            string normalizedPatientNumber,
            DateOnly dateOfBirth,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Only public booking identifies a patient this way.");

        public Task AddAsync(PatientEntity patient, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PatientEntity?> GetByIdAsync(
            Guid patientId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_patient is { IsDeleted: false } && _patient.Id == patientId ? _patient : null);

        public Task<PatientEntity?> GetTrackedByIdAsync(
            Guid patientId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_patient is { IsDeleted: false } && _patient.Id == patientId ? _patient : null);
    }

    private sealed class FakeAuditRepository : IPatientAuditRepository
    {
        public List<PatientAuditLog> Added { get; } = [];

        public Task AddAsync(PatientAuditLog auditLog, CancellationToken cancellationToken = default)
        {
            Added.Add(auditLog);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOutboxRepository : IOutboxMessageRepository
    {
        public List<OutboxMessage> Added { get; } = [];

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Added.Add(message);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetUnprocessedBatchAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutboxMessage>>([]);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task SaveRegistrationAsync(string duplicateNic, CancellationToken cancellationToken = default) =>
            SaveChangesAsync(cancellationToken);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
