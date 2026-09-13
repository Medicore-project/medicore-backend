using System.Text.Json;
using MediCore.Contracts.Events.Patient;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Interfaces;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Application.Services;

public sealed class PatientProfileService : IPatientProfileService
{
    private const string PatientEventsTopic = "patient-events";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IPatientRepository _patientRepository;
    private readonly IPatientAuditRepository _auditRepository;
    private readonly IOutboxMessageRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public PatientProfileService(
        IPatientRepository patientRepository,
        IPatientAuditRepository auditRepository,
        IOutboxMessageRepository outboxRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _patientRepository = patientRepository;
        _auditRepository = auditRepository;
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<PatientProfileResponse?> GetByIdAsync(
        Guid patientId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var patient = await _patientRepository.GetByIdAsync(patientId, cancellationToken);
        if (patient is null)
        {
            return null;
        }

        await AddAuditAsync(patient.Id, "PatientViewed", accessContext, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToProfileResponse(patient);
    }

    public async Task<PatientUpdateResult> UpdateAsync(
        Guid patientId,
        UpdatePatientRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var patient = await _patientRepository.GetTrackedByIdAsync(patientId, cancellationToken);
        if (patient is null)
        {
            return new PatientUpdateNotFoundResult();
        }

        patient.FirstName = request.FirstName.Trim();
        patient.LastName = request.LastName.Trim();
        patient.DateOfBirth = request.DateOfBirth;
        patient.Gender = PatientInputNormalizer.Gender(request.Gender);
        patient.Email = request.Email.Trim().ToLowerInvariant();
        patient.Phone = PatientInputNormalizer.Phone(request.Phone);
        patient.AddressLine1 = request.AddressLine1.Trim();
        patient.AddressLine2 = PatientInputNormalizer.OptionalText(request.AddressLine2);
        patient.District = request.District.Trim();
        patient.EmergencyContactName = PatientInputNormalizer.OptionalText(request.EmergencyContactName);
        patient.EmergencyContactPhone = PatientInputNormalizer.OptionalPhone(request.EmergencyContactPhone);
        patient.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        patient.UpdatedBy = NormalizeActorId(accessContext.ActorId);

        var updatedEvent = new PatientUpdatedEvent
        {
            PatientId = patient.Id,
            FullName = patient.FullName,
            CorrelationId = accessContext.CorrelationId,
            OccurredAtUtc = patient.UpdatedAt.Value
        };

        await _outboxRepository.AddAsync(new OutboxMessage
        {
            MessageId = updatedEvent.MessageId,
            Topic = PatientEventsTopic,
            EventKey = patient.Id.ToString(),
            EventType = updatedEvent.EventType,
            EventVersion = updatedEvent.Version,
            CorrelationId = updatedEvent.CorrelationId,
            Payload = JsonSerializer.Serialize(updatedEvent, SerializerOptions),
            OccurredOnUtc = updatedEvent.OccurredAtUtc
        }, cancellationToken);

        await AddAuditAsync(patient.Id, "PatientUpdated", accessContext, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new PatientUpdatedResult(ToProfileResponse(patient));
    }

    public async Task<bool> DeleteAsync(
        Guid patientId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default)
    {
        var patient = await _patientRepository.GetTrackedByIdAsync(patientId, cancellationToken);
        if (patient is null)
        {
            return false;
        }

        patient.IsDeleted = true;
        patient.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        patient.UpdatedBy = NormalizeActorId(accessContext.ActorId);

        await AddAuditAsync(patient.Id, "PatientDeleted", accessContext, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task AddAuditAsync(
        Guid patientId,
        string action,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken)
    {
        return _auditRepository.AddAsync(new PatientAuditLog
        {
            PatientId = patientId,
            ActorId = NormalizeActorId(accessContext.ActorId),
            ActorRole = string.IsNullOrWhiteSpace(accessContext.ActorRole)
                ? "Unknown"
                : accessContext.ActorRole.Trim(),
            Action = action,
            CorrelationId = accessContext.CorrelationId,
            IpAddress = string.IsNullOrWhiteSpace(accessContext.IpAddress)
                ? null
                : accessContext.IpAddress.Trim(),
            OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        }, cancellationToken);
    }

    private static string NormalizeActorId(string actorId) =>
        string.IsNullOrWhiteSpace(actorId) ? "system" : actorId.Trim();

    private static PatientProfileResponse ToProfileResponse(PatientEntity patient) => new(
        patient.Id,
        patient.PatientNumber,
        patient.Nic,
        patient.FirstName,
        patient.LastName,
        patient.FullName,
        patient.DateOfBirth,
        patient.Gender,
        patient.Email,
        patient.Phone,
        patient.AddressLine1,
        patient.AddressLine2,
        patient.District,
        patient.EmergencyContactName,
        patient.EmergencyContactPhone,
        patient.CreatedAt,
        patient.UpdatedAt);
}
