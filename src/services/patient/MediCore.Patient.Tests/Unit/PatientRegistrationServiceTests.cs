using System.Text.Json;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Exceptions;
using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Application.Services;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Tests.Unit;

public sealed class PatientRegistrationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Registration_creates_patient_and_outbox_event_atomically()
    {
        var patients = new FakePatientRepository();
        var outbox = new FakeOutboxRepository();
        var unitOfWork = new FakeUnitOfWork(() => patients.Added!.PatientNumber = "PAT-000001");
        var service = CreateService(patients, outbox, unitOfWork);

        var result = await service.RegisterAsync(ValidRequest(), "corr-123", "receptionist-1");

        var registered = Assert.IsType<PatientRegisteredResult>(result);
        Assert.Equal("PAT-000001", registered.Patient.PatientNumber);
        Assert.Equal("200012345678", patients.Added!.Nic);
        Assert.Equal("patient-events", outbox.Added!.Topic);
        Assert.Equal("patient.registered", outbox.Added.EventType);
        Assert.Equal(patients.Added.Id.ToString(), outbox.Added.EventKey);
        Assert.Equal("corr-123", outbox.Added.CorrelationId);
        Assert.NotEqual(Guid.Empty, outbox.Added.MessageId);
        Assert.Equal(1, unitOfWork.SaveCount);

        using var payload = JsonDocument.Parse(outbox.Added.Payload);
        Assert.Equal(patients.Added.Id, payload.RootElement.GetProperty("patientId").GetGuid());
        Assert.Equal("patient.registered", payload.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(outbox.Added.MessageId, payload.RootElement.GetProperty("messageId").GetGuid());
    }

    [Fact]
    public async Task Existing_nic_returns_existing_patient_without_writing()
    {
        var existing = ExistingPatient();
        var patients = new FakePatientRepository { Existing = existing };
        var outbox = new FakeOutboxRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(patients, outbox, unitOfWork);

        var result = await service.RegisterAsync(ValidRequest(), "corr-123", "receptionist-1");

        var duplicate = Assert.IsType<DuplicatePatientResult>(result);
        Assert.Equal(existing.Id, duplicate.ExistingPatient.PatientId);
        Assert.Null(patients.Added);
        Assert.Null(outbox.Added);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Concurrent_unique_violation_is_translated_to_duplicate_result()
    {
        var existing = ExistingPatient();
        var patients = new FakePatientRepository();
        var outbox = new FakeOutboxRepository();
        var unitOfWork = new FakeUnitOfWork(() =>
        {
            patients.Existing = existing;
            throw new DuplicateNicException("200012345678");
        });
        var service = CreateService(patients, outbox, unitOfWork);

        var result = await service.RegisterAsync(ValidRequest(), "corr-123", "receptionist-1");

        var duplicate = Assert.IsType<DuplicatePatientResult>(result);
        Assert.Equal(existing.PatientNumber, duplicate.ExistingPatient.PatientNumber);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    private static PatientRegistrationService CreateService(
        IPatientRepository patients,
        IOutboxMessageRepository outbox,
        IUnitOfWork unitOfWork) =>
        new(patients, outbox, unitOfWork, new FixedTimeProvider(Now));

    private static CreatePatientRequest ValidRequest() => new(
        " 200012345678 ",
        "Nimali",
        "Perera",
        new DateOnly(2000, 5, 15),
        "Female",
        "NIMALI@example.com",
        "077 123 4567",
        "12 Hospital Road",
        null,
        "Colombo",
        null,
        null);

    private static PatientEntity ExistingPatient() => new()
    {
        Id = Guid.NewGuid(),
        PatientNumber = "PAT-000099",
        Nic = "200012345678",
        FirstName = "Existing",
        LastName = "Patient",
        Email = "existing@example.com"
    };

    private sealed class FakePatientRepository : IPatientRepository
    {
        public PatientEntity? Existing { get; set; }
        public PatientEntity? Added { get; private set; }

        public Task<PatientEntity?> FindByNicAsync(
            string normalizedNic,
            bool includeArchived,
            CancellationToken cancellationToken = default) => Task.FromResult(Existing);

        public Task AddAsync(
            PatientEntity patient,
            CancellationToken cancellationToken = default)
        {
            Added = patient;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOutboxRepository : IOutboxMessageRepository
    {
        public OutboxMessage? Added { get; private set; }

        public Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            Added = message;
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
        private readonly Action? _onSave;

        public FakeUnitOfWork(Action? onSave = null)
        {
            _onSave = onSave;
        }

        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(string duplicateNic, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            _onSave?.Invoke();
            return Task.CompletedTask;
        }
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
