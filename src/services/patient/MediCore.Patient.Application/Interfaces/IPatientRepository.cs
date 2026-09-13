using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Application.Interfaces;

public interface IPatientRepository
{
    Task<PatientEntity?> FindByNicAsync(
        string normalizedNic,
        bool includeArchived,
        CancellationToken cancellationToken = default);

    Task AddAsync(PatientEntity patient, CancellationToken cancellationToken = default);

    Task<PatientEntity?> GetByIdAsync(Guid patientId, CancellationToken cancellationToken = default);

    Task<PatientEntity?> GetTrackedByIdAsync(Guid patientId, CancellationToken cancellationToken = default);
}
