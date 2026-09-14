using System.Text.Json;
using MediCore.Contracts.Events.Appointment;
using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Exceptions;
using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Application.Services;
using MedicalRecordEntity = MediCore.Patient.Application.Entities.MedicalRecord;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Patient.Tests.Unit;

public sealed class AppointmentCompletedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Valid_completion_appends_visit_audit_and_processed_marker_atomically()
    {
        var patient = ExistingPatient();
        var fixture = new Fixture(patient);
        var completedEvent = CompletedEvent(patient.Id);

        var result = await fixture.Handler.HandleAsync(completedEvent);

        Assert.Equal(AppointmentCompletedHandlingResult.Processed, result);
        var record = Assert.Single(fixture.Records.Added);
        Assert.Equal(patient.Id, record.PatientId);
        Assert.Equal(completedEvent.AppointmentId, record.VisitReference);
        Assert.Equal("Completed appointment notes.", record.ClinicalNotes);
        Assert.Equal(completedEvent.OccurredAtUtc, record.AuthoredAtUtc);
        Assert.Equal("appointment-service", record.AuthorClinicianId);
        Assert.Equal("appointment-service@internal", record.AuthorClinicianEmail);
        Assert.Equal("System", record.AuthorClinicianRole);
        Assert.Equal("appointment-service", record.CreatedBy);

        var processed = Assert.Single(fixture.Processed.Added);
        Assert.Equal(completedEvent.MessageId, processed.MessageId);
        Assert.Equal("appointment.completed", processed.EventType);
        Assert.Equal("medicore-patient", processed.ConsumerGroup);
        Assert.Equal("appointment-events", processed.SourceTopic);
        Assert.Equal(Now.UtcDateTime, processed.ProcessedAtUtc);

        var audit = Assert.Single(fixture.Audits.Added);
        Assert.Equal(patient.Id, audit.PatientId);
        Assert.Equal($"AppointmentVisitImported:{completedEvent.AppointmentId}", audit.Action);
        Assert.Equal(completedEvent.CorrelationId, audit.CorrelationId);
        Assert.Empty(fixture.Outbox.Added);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Redelivered_message_is_skipped_without_duplicate_writes()
    {
        var patient = ExistingPatient();
        var fixture = new Fixture(patient);
        var completedEvent = CompletedEvent(patient.Id);

        var firstResult = await fixture.Handler.HandleAsync(completedEvent);
        var replayResult = await fixture.Handler.HandleAsync(completedEvent);

        Assert.Equal(AppointmentCompletedHandlingResult.Processed, firstResult);
        Assert.Equal(AppointmentCompletedHandlingResult.Duplicate, replayResult);
        Assert.Single(fixture.Records.Added);
        Assert.Single(fixture.Processed.Added);
        Assert.Single(fixture.Audits.Added);
        Assert.Empty(fixture.Outbox.Added);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Unknown_patient_is_marked_processed_and_queued_to_dead_letter_outbox()
    {
        var fixture = new Fixture(null);
        var completedEvent = CompletedEvent(Guid.NewGuid());

        var result = await fixture.Handler.HandleAsync(completedEvent);

        Assert.Equal(AppointmentCompletedHandlingResult.DeadLetterQueued, result);
        Assert.Empty(fixture.Records.Added);
        Assert.Empty(fixture.Audits.Added);
        Assert.Equal(completedEvent.MessageId, Assert.Single(fixture.Processed.Added).MessageId);

        var deadLetter = Assert.Single(fixture.Outbox.Added);
        Assert.Equal("appointment-events.dlt", deadLetter.Topic);
        Assert.Equal("appointment.completed.dead-lettered", deadLetter.EventType);
        Assert.Equal(completedEvent.AppointmentId.ToString(), deadLetter.EventKey);
        Assert.Equal(completedEvent.CorrelationId, deadLetter.CorrelationId);
        using var payload = JsonDocument.Parse(deadLetter.Payload);
        Assert.Equal("UnknownPatient", payload.RootElement.GetProperty("reason").GetString());
        Assert.Equal(
            completedEvent.MessageId,
            payload.RootElement.GetProperty("originalEvent").GetProperty("messageId").GetGuid());
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task Redelivered_unknown_patient_does_not_queue_duplicate_dead_letter()
    {
        var fixture = new Fixture(null);
        var completedEvent = CompletedEvent(Guid.NewGuid());

        var firstResult = await fixture.Handler.HandleAsync(completedEvent);
        var replayResult = await fixture.Handler.HandleAsync(completedEvent);

        Assert.Equal(AppointmentCompletedHandlingResult.DeadLetterQueued, firstResult);
        Assert.Equal(AppointmentCompletedHandlingResult.Duplicate, replayResult);
        Assert.Single(fixture.Outbox.Added);
        Assert.Single(fixture.Processed.Added);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Theory]
    [InlineData("message")]
    [InlineData("appointment")]
    [InlineData("patient")]
    [InlineData("notes")]
    public async Task Invalid_event_is_queued_to_dead_letter(string invalidField)
    {
        var patient = ExistingPatient();
        var source = CompletedEvent(patient.Id);
        var completedEvent = source with
        {
            MessageId = invalidField == "message" ? Guid.Empty : source.MessageId,
            AppointmentId = invalidField == "appointment" ? Guid.Empty : source.AppointmentId,
            PatientId = invalidField == "patient" ? Guid.Empty : source.PatientId,
            Notes = invalidField == "notes" ? " " : source.Notes
        };
        var fixture = new Fixture(patient);

        var result = await fixture.Handler.HandleAsync(completedEvent);

        Assert.Equal(AppointmentCompletedHandlingResult.DeadLetterQueued, result);
        Assert.Empty(fixture.Records.Added);
        Assert.Single(fixture.Outbox.Added);
        Assert.Single(fixture.Processed.Added);
    }

    [Fact]
    public async Task Unique_constraint_race_is_treated_as_duplicate_delivery()
    {
        var patient = ExistingPatient();
        var fixture = new Fixture(patient);
        fixture.UnitOfWork.SaveException = new DuplicateProcessedMessageException();

        var result = await fixture.Handler.HandleAsync(CompletedEvent(patient.Id));

        Assert.Equal(AppointmentCompletedHandlingResult.Duplicate, result);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    private static AppointmentCompletedEvent CompletedEvent(Guid patientId) => new()
    {
        MessageId = Guid.NewGuid(),
        AppointmentId = Guid.NewGuid(),
        PatientId = patientId,
        Notes = "  Completed appointment notes.  ",
        CorrelationId = "corr-27",
        OccurredAtUtc = Now.AddMinutes(-15).UtcDateTime,
        Version = 1
    };

    private static PatientEntity ExistingPatient() => new()
    {
        Id = Guid.NewGuid(),
        PatientNumber = "PAT-000027",
        Nic = "200012345678",
        FirstName = "Test",
        LastName = "Patient"
    };

    private sealed class Fixture
    {
        public Fixture(PatientEntity? patient)
        {
            Records = new FakeMedicalRecordRepository();
            Processed = new FakeProcessedMessageRepository();
            Audits = new FakeAuditRepository();
            Outbox = new FakeOutboxRepository();
            UnitOfWork = new FakeUnitOfWork();
            Handler = new AppointmentCompletedHandler(
                new FakePatientRepository(patient),
                Processed,
                Records,
                Audits,
                Outbox,
                UnitOfWork,
                new FixedTimeProvider(Now),
                NullLogger<AppointmentCompletedHandler>.Instance);
        }

        public FakeMedicalRecordRepository Records { get; }
        public FakeProcessedMessageRepository Processed { get; }
        public FakeAuditRepository Audits { get; }
        public FakeOutboxRepository Outbox { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public AppointmentCompletedHandler Handler { get; }
    }

    private sealed class FakePatientRepository(PatientEntity? patient) : IPatientRepository
    {
        public Task<PatientEntity?> FindByNicAsync(string normalizedNic, bool includeArchived, CancellationToken cancellationToken = default) =>
            Task.FromResult(patient);

        public Task AddAsync(PatientEntity value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PatientEntity?> GetByIdAsync(Guid patientId, CancellationToken cancellationToken = default) =>
            Task.FromResult(patient is { IsDeleted: false } && patient.Id == patientId ? patient : null);

        public Task<PatientEntity?> GetTrackedByIdAsync(Guid patientId, CancellationToken cancellationToken = default) =>
            GetByIdAsync(patientId, cancellationToken);
    }

    private sealed class FakeProcessedMessageRepository : IProcessedMessageRepository
    {
        public HashSet<Guid> ExistingMessageIds { get; } = [];
        public List<ProcessedMessage> Added { get; } = [];

        public Task<bool> ExistsAsync(Guid messageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingMessageIds.Contains(messageId));

        public Task AddAsync(ProcessedMessage message, CancellationToken cancellationToken = default)
        {
            Added.Add(message);
            ExistingMessageIds.Add(message.MessageId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMedicalRecordRepository : IMedicalRecordRepository
    {
        public List<MedicalRecordEntity> Added { get; } = [];

        public Task AddAsync(MedicalRecordEntity record, CancellationToken cancellationToken = default)
        {
            Added.Add(record);
            return Task.CompletedTask;
        }

        public Task<MedicalRecordEntity?> GetCurrentAsync(Guid patientId, Guid recordId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MedicalRecordEntity?>(null);

        public Task<MedicalRecordEntity?> GetTrackedCurrentAsync(Guid patientId, Guid recordId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MedicalRecordEntity?>(null);

        public Task<(IReadOnlyList<MedicalRecordEntity> Items, int TotalCount)> GetCurrentPageAsync(
            Guid patientId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<MedicalRecordEntity>, int)>(([], 0));

        public Task<IReadOnlyList<MedicalRecordEntity>> GetVersionsAsync(
            Guid patientId,
            Guid recordId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MedicalRecordEntity>>([]);
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
        public Exception? SaveException { get; set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return SaveException is null ? Task.CompletedTask : Task.FromException(SaveException);
        }

        public Task SaveRegistrationAsync(string duplicateNic, CancellationToken cancellationToken = default) =>
            SaveChangesAsync(cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
