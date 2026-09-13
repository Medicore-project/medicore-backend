using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Services;

public interface IMedicalRecordService
{
    Task<PagedMedicalRecordResponse?> GetPageAsync(
        Guid patientId,
        MedicalRecordListRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    Task<MedicalRecordResponse?> GetByIdAsync(
        Guid patientId,
        Guid recordId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MedicalRecordResponse>?> GetVersionsAsync(
        Guid patientId,
        Guid recordId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    Task<MedicalRecordCreateResult> CreateAsync(
        Guid patientId,
        CreateMedicalRecordRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    Task<MedicalRecordUpdateResult> UpdateAsync(
        Guid patientId,
        Guid recordId,
        UpdateMedicalRecordRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid patientId,
        Guid recordId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);
}

public abstract record MedicalRecordCreateResult;
public sealed record MedicalRecordCreatedResult(MedicalRecordResponse Record) : MedicalRecordCreateResult;
public sealed record MedicalRecordCreatePatientNotFoundResult : MedicalRecordCreateResult;

public abstract record MedicalRecordUpdateResult;
public sealed record MedicalRecordUpdatedResult(MedicalRecordResponse Record) : MedicalRecordUpdateResult;
public sealed record MedicalRecordUpdateNotFoundResult : MedicalRecordUpdateResult;
public sealed record MedicalRecordUpdateConflictResult(int CurrentVersion) : MedicalRecordUpdateResult;
