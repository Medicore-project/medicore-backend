using System.Text.Json;
using MediCore.Contracts.Events.Appointment;
using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Exceptions;
using MediCore.Patient.Application.Interfaces;
using MedicalRecordEntity = MediCore.Patient.Application.Entities.MedicalRecord;
using Microsoft.Extensions.Logging;

namespace MediCore.Patient.Application.Services;

public sealed class AppointmentCompletedHandler : IAppointmentCompletedHandler
{
    public const string ConsumerGroup = "medicore-patient";
    public const string SourceTopic = "appointment-events";
    public const string DeadLetterTopic = "appointment-events.dlt";

    private const string IntegrationActorId = "appointment-service";
    private const string IntegrationActorEmail = "appointment-service@internal";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IPatientRepository _patientRepository;
    private readonly IProcessedMessageRepository _processedMessageRepository;
    private readonly IMedicalRecordRepository _medicalRecordRepository;
    private readonly IPatientAuditRepository _auditRepository;
    private readonly IOutboxMessageRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentCompletedHandler> _logger;

    public AppointmentCompletedHandler(
        IPatientRepository patientRepository,
        IProcessedMessageRepository processedMessageRepository,
        IMedicalRecordRepository medicalRecordRepository,
        IPatientAuditRepository auditRepository,
        IOutboxMessageRepository outboxRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<AppointmentCompletedHandler> logger)
    {
        _patientRepository = patientRepository;
        _processedMessageRepository = processedMessageRepository;
        _medicalRecordRepository = medicalRecordRepository;
        _auditRepository = auditRepository;
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AppointmentCompletedHandlingResult> HandleAsync(
        AppointmentCompletedEvent completedEvent,
        CancellationToken cancellationToken = default)
    {
        if (await _processedMessageRepository.ExistsAsync(completedEvent.MessageId, cancellationToken))
        {
            _logger.LogInformation(
                "Skipping duplicate {EventType} message {MessageId}.",
                completedEvent.EventType,
                completedEvent.MessageId);
            return AppointmentCompletedHandlingResult.Duplicate;
        }

        var validationFailure = Validate(completedEvent);
        if (validationFailure is not null)
        {
            return await QueueDeadLetterAsync(completedEvent, validationFailure, cancellationToken);
        }

        var patient = await _patientRepository.GetByIdAsync(completedEvent.PatientId, cancellationToken);
        if (patient is null)
        {
            _logger.LogWarning(
                "Appointment completion {MessageId} references unknown patient {PatientId}; queuing to DLT.",
                completedEvent.MessageId,
                completedEvent.PatientId);
            return await QueueDeadLetterAsync(completedEvent, "UnknownPatient", cancellationToken);
        }

        var authoredAtUtc = EnsureUtc(completedEvent.OccurredAtUtc);
        var record = new MedicalRecordEntity
        {
            PatientId = patient.Id,
            VisitReference = completedEvent.AppointmentId,
            ClinicalNotes = completedEvent.Notes.Trim(),
            AuthorClinicianId = IntegrationActorId,
            AuthorClinicianEmail = IntegrationActorEmail,
            AuthorClinicianRole = "System",
            AuthoredAtUtc = authoredAtUtc,
            CreatedBy = IntegrationActorId
        };

        await _medicalRecordRepository.AddAsync(record, cancellationToken);
        await _auditRepository.AddAsync(new PatientAuditLog
        {
            PatientId = patient.Id,
            ActorId = IntegrationActorId,
            ActorRole = "System",
            Action = $"AppointmentVisitImported:{completedEvent.AppointmentId}",
            CorrelationId = NormalizeCorrelationId(completedEvent.CorrelationId),
            OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        }, cancellationToken);
        await AddProcessedMessageAsync(completedEvent, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Imported appointment {AppointmentId} into patient {PatientId} medical record from message {MessageId}.",
                completedEvent.AppointmentId,
                completedEvent.PatientId,
                completedEvent.MessageId);
            return AppointmentCompletedHandlingResult.Processed;
        }
        catch (DuplicateProcessedMessageException)
        {
            return AppointmentCompletedHandlingResult.Duplicate;
        }
    }

    private async Task<AppointmentCompletedHandlingResult> QueueDeadLetterAsync(
        AppointmentCompletedEvent completedEvent,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _logger.LogWarning(
            "Dead-lettering appointment completion {MessageId}. Reason: {DeadLetterReason}.",
            completedEvent.MessageId,
            reason);
        var deadLetter = new AppointmentCompletedDeadLetter(
            completedEvent,
            reason,
            SourceTopic,
            ConsumerGroup,
            now);

        await _outboxRepository.AddAsync(new OutboxMessage
        {
            MessageId = Guid.NewGuid(),
            Topic = DeadLetterTopic,
            EventKey = completedEvent.AppointmentId.ToString(),
            EventType = "appointment.completed.dead-lettered",
            EventVersion = completedEvent.Version,
            CorrelationId = NormalizeCorrelationId(completedEvent.CorrelationId),
            Payload = JsonSerializer.Serialize(deadLetter, SerializerOptions),
            OccurredOnUtc = now
        }, cancellationToken);
        await AddProcessedMessageAsync(completedEvent, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return AppointmentCompletedHandlingResult.DeadLetterQueued;
        }
        catch (DuplicateProcessedMessageException)
        {
            return AppointmentCompletedHandlingResult.Duplicate;
        }
    }

    private Task AddProcessedMessageAsync(
        AppointmentCompletedEvent completedEvent,
        CancellationToken cancellationToken) =>
        _processedMessageRepository.AddAsync(new ProcessedMessage
        {
            MessageId = completedEvent.MessageId,
            EventType = completedEvent.EventType,
            ConsumerGroup = ConsumerGroup,
            SourceTopic = SourceTopic,
            ProcessedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        }, cancellationToken);

    private static string? Validate(AppointmentCompletedEvent completedEvent)
    {
        if (completedEvent.MessageId == Guid.Empty) return "MissingMessageId";
        if (completedEvent.AppointmentId == Guid.Empty) return "MissingAppointmentId";
        if (completedEvent.PatientId == Guid.Empty) return "MissingPatientId";
        if (string.IsNullOrWhiteSpace(completedEvent.Notes)) return "MissingNotes";
        if (completedEvent.Notes.Trim().Length > 8_000) return "NotesTooLong";
        if (completedEvent.Version != 1) return "UnsupportedEventVersion";
        return null;
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string NormalizeCorrelationId(string correlationId) =>
        string.IsNullOrWhiteSpace(correlationId) ? "unknown" : correlationId.Trim();

    private sealed record AppointmentCompletedDeadLetter(
        AppointmentCompletedEvent OriginalEvent,
        string Reason,
        string SourceTopic,
        string ConsumerGroup,
        DateTime FailedAtUtc);
}
