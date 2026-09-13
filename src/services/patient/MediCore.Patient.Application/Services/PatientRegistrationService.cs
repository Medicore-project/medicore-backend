using System.Text.Json;
using MediCore.Contracts.Events.Patient;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Exceptions;
using MediCore.Patient.Application.Interfaces;

namespace MediCore.Patient.Application.Services;

public sealed class PatientRegistrationService : IPatientRegistrationService
{
    private const string PatientEventsTopic = "patient-events";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IPatientRepository _patientRepository;
    private readonly IOutboxMessageRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public PatientRegistrationService(
        IPatientRepository patientRepository,
        IOutboxMessageRepository outboxRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _patientRepository = patientRepository;
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<PatientRegistrationResult> RegisterAsync(
        CreatePatientRequest request,
        string correlationId,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var normalizedNic = NormalizeNic(request.Nic);
        var existingPatient = await _patientRepository.FindByNicAsync(
            normalizedNic,
            includeArchived: true,
            cancellationToken);

        if (existingPatient is not null)
        {
            return new DuplicatePatientResult(ToExistingPatientSummary(existingPatient));
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var patient = new Entities.Patient
        {
            Nic = normalizedNic,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Gender = NormalizeGender(request.Gender),
            Email = request.Email.Trim().ToLowerInvariant(),
            Phone = NormalizePhone(request.Phone),
            AddressLine1 = request.AddressLine1.Trim(),
            AddressLine2 = NullIfWhiteSpace(request.AddressLine2),
            District = request.District.Trim(),
            EmergencyContactName = NullIfWhiteSpace(request.EmergencyContactName),
            EmergencyContactPhone = NormalizeOptionalPhone(request.EmergencyContactPhone),
            CreatedAt = now,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim()
        };

        var registeredEvent = new PatientRegisteredEvent
        {
            PatientId = patient.Id,
            FullName = patient.FullName,
            Email = patient.Email,
            CorrelationId = correlationId,
            OccurredAtUtc = now
        };

        var outboxMessage = new OutboxMessage
        {
            MessageId = registeredEvent.MessageId,
            Topic = PatientEventsTopic,
            EventKey = patient.Id.ToString(),
            EventType = registeredEvent.EventType,
            EventVersion = registeredEvent.Version,
            CorrelationId = registeredEvent.CorrelationId,
            Payload = JsonSerializer.Serialize(registeredEvent, SerializerOptions),
            OccurredOnUtc = registeredEvent.OccurredAtUtc
        };

        await _patientRepository.AddAsync(patient, cancellationToken);
        await _outboxRepository.AddAsync(outboxMessage, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(normalizedNic, cancellationToken);
        }
        catch (DuplicateNicException)
        {
            existingPatient = await _patientRepository.FindByNicAsync(
                normalizedNic,
                includeArchived: true,
                cancellationToken);

            if (existingPatient is null)
            {
                throw;
            }

            return new DuplicatePatientResult(ToExistingPatientSummary(existingPatient));
        }

        if (string.IsNullOrWhiteSpace(patient.PatientNumber))
        {
            throw new InvalidOperationException("The database did not generate a patient number.");
        }

        return new PatientRegisteredResult(ToRegistrationResponse(patient));
    }

    private static PatientRegistrationResponse ToRegistrationResponse(Entities.Patient patient) =>
        new(
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
            patient.CreatedAt);

    private static ExistingPatientSummary ToExistingPatientSummary(Entities.Patient patient) =>
        new(patient.Id, patient.PatientNumber, patient.FullName, patient.Email, patient.IsDeleted);

    internal static string NormalizeNic(string nic) => nic.Trim().ToUpperInvariant();

    internal static string NormalizePhone(string phone) =>
        phone.Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .Trim();

    private static string? NormalizeOptionalPhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : NormalizePhone(phone);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeGender(string gender) => gender.Trim().ToLowerInvariant() switch
    {
        "male" => "Male",
        "female" => "Female",
        "other" => "Other",
        "prefer not to say" or "prefernottosay" => "PreferNotToSay",
        _ => gender.Trim()
    };
}
