using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Services;

public interface IPatientProfileService
{
    Task<PatientProfileResponse?> GetByIdAsync(
        Guid patientId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    Task<PatientUpdateResult> UpdateAsync(
        Guid patientId,
        UpdatePatientRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid patientId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);
}

public abstract record PatientUpdateResult;

public sealed record PatientUpdatedResult(PatientProfileResponse Patient) : PatientUpdateResult;

public sealed record PatientUpdateNotFoundResult : PatientUpdateResult;
