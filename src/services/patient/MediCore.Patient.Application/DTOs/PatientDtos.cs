namespace MediCore.Patient.Application.DTOs;

public sealed record CreatePatientRequest(
    string Nic,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string Gender,
    string Email,
    string Phone,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string? EmergencyContactName,
    string? EmergencyContactPhone);

public sealed record PatientRegistrationResponse(
    Guid PatientId,
    string PatientNumber,
    string Nic,
    string FirstName,
    string LastName,
    string FullName,
    DateOnly DateOfBirth,
    string Gender,
    string Email,
    string Phone,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    DateTime CreatedAt);

public sealed record ExistingPatientSummary(
    Guid PatientId,
    string PatientNumber,
    string FullName,
    string Email,
    bool IsArchived);

public sealed record DuplicatePatientResponse(
    string Message,
    ExistingPatientSummary ExistingPatient);

public sealed record UpdatePatientRequest(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string Gender,
    string Email,
    string Phone,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string? EmergencyContactName,
    string? EmergencyContactPhone);

public sealed record PatientProfileResponse(
    Guid PatientId,
    string PatientNumber,
    string Nic,
    string FirstName,
    string LastName,
    string FullName,
    DateOnly DateOfBirth,
    string Gender,
    string Email,
    string Phone,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record PatientAccessContext(
    string ActorId,
    string ActorRole,
    string CorrelationId,
    string? IpAddress,
    string? ActorEmail = null);
