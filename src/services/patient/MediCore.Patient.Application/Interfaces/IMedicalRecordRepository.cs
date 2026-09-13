using MedicalRecordEntity = MediCore.Patient.Application.Entities.MedicalRecord;

namespace MediCore.Patient.Application.Interfaces;

public interface IMedicalRecordRepository
{
    Task AddAsync(MedicalRecordEntity record, CancellationToken cancellationToken = default);

    Task<MedicalRecordEntity?> GetCurrentAsync(
        Guid patientId,
        Guid recordId,
        CancellationToken cancellationToken = default);

    Task<MedicalRecordEntity?> GetTrackedCurrentAsync(
        Guid patientId,
        Guid recordId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<MedicalRecordEntity> Items, int TotalCount)> GetCurrentPageAsync(
        Guid patientId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MedicalRecordEntity>> GetVersionsAsync(
        Guid patientId,
        Guid recordId,
        CancellationToken cancellationToken = default);
}
