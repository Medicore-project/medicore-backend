using MediCore.Patient.Application.Entities;
using PrescriptionEntity = MediCore.Patient.Application.Entities.Prescription;

namespace MediCore.Patient.Application.Interfaces;

/// <summary>
/// Data-access contract for <see cref="PrescriptionEntity"/>.
/// Follows the same pattern as <see cref="IMedicalRecordRepository"/>.
/// </summary>
public interface IPrescriptionRepository
{
    /// <summary>Stages a new prescription for insertion (not yet committed).</summary>
    Task AddAsync(PrescriptionEntity prescription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a non-tracked prescription by its business key, or null if not found
    /// (including soft-deleted rows, which are filtered by the global query filter).
    /// </summary>
    Task<PrescriptionEntity?> GetByIdAsync(
        Guid patientId,
        Guid prescriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a change-tracked prescription for mutation operations (update, complete,
    /// delete). Excludes soft-deleted rows via global query filter.
    /// </summary>
    Task<PrescriptionEntity?> GetTrackedByIdAsync(
        Guid patientId,
        Guid prescriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>All active (non-completed, non-deleted) prescriptions for a patient.</summary>
    Task<IReadOnlyList<PrescriptionEntity>> GetActiveAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>All completed (historical) prescriptions for a patient.</summary>
    Task<IReadOnlyList<PrescriptionEntity>> GetHistoryAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);
}
