using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Services;

public interface IPatientRegistrationService
{
    Task<PatientRegistrationResult> RegisterAsync(
        CreatePatientRequest request,
        string correlationId,
        string createdBy,
        CancellationToken cancellationToken = default);
}

public abstract record PatientRegistrationResult;

public sealed record PatientRegisteredResult(PatientRegistrationResponse Patient)
    : PatientRegistrationResult;

public sealed record DuplicatePatientResult(ExistingPatientSummary ExistingPatient)
    : PatientRegistrationResult;
