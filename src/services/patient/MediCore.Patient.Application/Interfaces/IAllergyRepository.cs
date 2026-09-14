using MediCore.Patient.Application.Entities;
using AllergyEntity = MediCore.Patient.Application.Entities.Allergy;

namespace MediCore.Patient.Application.Interfaces;

/// <summary>
/// Data-access contract for the <see cref="AllergyEntity"/> aggregate.
/// Follows the same pattern as <see cref="IPrescriptionRepository"/>.
/// </summary>
public interface IAllergyRepository
{
    /// <summary>Stages a new allergy for insertion (not yet committed).</summary>
    Task AddAsync(AllergyEntity allergy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a non-tracked allergy by its business key, or null when not found
    /// or soft-deleted (global query filter excludes IsDeleted = true).
    /// </summary>
    Task<AllergyEntity?> GetByIdAsync(
        Guid patientId,
        Guid allergyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a change-tracked allergy for mutation operations (update, deactivate, delete).
    /// Excludes soft-deleted rows via the global query filter.
    /// </summary>
    Task<AllergyEntity?> GetTrackedByIdAsync(
        Guid patientId,
        Guid allergyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all active + inactive (non-deleted) allergies for a patient,
    /// ordered by <c>RecordedAtUtc</c> descending.
    /// </summary>
    Task<IReadOnlyList<AllergyEntity>> GetAllAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all allergies in the <c>Active</c> status for a patient,
    /// ordered by <c>RecordedAtUtc</c> descending.
    /// Optimised by the <c>ix_allergies_patient_status</c> composite index.
    /// </summary>
    Task<IReadOnlyList<AllergyEntity>> GetActiveAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the first active allergy whose <c>Allergen</c> matches the supplied
    /// <paramref name="drug"/> using a case-insensitive substring check, or
    /// <c>null</c> when no conflict exists.
    /// Called by <see cref="MediCore.Patient.Application.Services.PrescriptionService"/>
    /// before persisting a new prescription.
    /// </summary>
    Task<AllergyEntity?> CheckConflictAsync(
        Guid patientId,
        string drug,
        CancellationToken cancellationToken = default);
}
