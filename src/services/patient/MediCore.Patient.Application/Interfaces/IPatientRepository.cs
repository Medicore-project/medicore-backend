using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Application.Interfaces;

public interface IPatientRepository
{
    Task<PatientEntity?> FindByNicAsync(
        string normalizedNic,
        bool includeArchived,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The one active patient with this number and date of birth, or null. Used by the public
    /// booking flow to identify a returning patient who has no account.
    /// </summary>
    /// <remarks>
    /// The date of birth is a second factor, not a filter: patient numbers come from a sequence
    /// and are trivially enumerable on their own. Archived patients never match — a removed record
    /// must not be bookable.
    /// </remarks>
    Task<PatientEntity?> FindByPatientNumberAndDateOfBirthAsync(
        string normalizedPatientNumber,
        DateOnly dateOfBirth,
        CancellationToken cancellationToken = default);

    Task AddAsync(PatientEntity patient, CancellationToken cancellationToken = default);

    Task<PatientEntity?> GetByIdAsync(Guid patientId, CancellationToken cancellationToken = default);

    Task<PatientEntity?> GetTrackedByIdAsync(Guid patientId, CancellationToken cancellationToken = default);
}
