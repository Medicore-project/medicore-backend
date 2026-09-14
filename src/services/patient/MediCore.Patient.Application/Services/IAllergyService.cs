using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Services;

// ── Discriminated union results ───────────────────────────────────────────────

public abstract record AllergyCreateResult;
public sealed record AllergyCreatedResult(AllergyResponse Allergy) : AllergyCreateResult;
public sealed record AllergyCreatePatientNotFoundResult : AllergyCreateResult;

public abstract record AllergyUpdateResult;
public sealed record AllergyUpdatedResult(AllergyResponse Allergy) : AllergyUpdateResult;
public sealed record AllergyUpdateNotFoundResult : AllergyUpdateResult;

public abstract record AllergyDeactivateResult;
public sealed record AllergyDeactivatedResult(AllergyResponse Allergy) : AllergyDeactivateResult;
public sealed record AllergyDeactivateNotFoundResult : AllergyDeactivateResult;

/// <summary>
/// Returned when <c>PATCH .../deactivate</c> is called on an allergy
/// that is already in the <c>Inactive</c> state.
/// </summary>
public sealed record AllergyDeactivateAlreadyInactiveResult : AllergyDeactivateResult;

// ── Service contract ──────────────────────────────────────────────────────────

/// <summary>
/// Application-layer service for the Allergy aggregate.
/// Owns all business logic: creation, field updates, status transition
/// (Active → Inactive), soft-delete, conflict check, and audit trail.
/// </summary>
public interface IAllergyService
{
    /// <summary>
    /// Returns all allergies (active + inactive) for a patient, ordered newest-first.
    /// Returns null when the patient does not exist.
    /// </summary>
    Task<IReadOnlyList<AllergyResponse>?> GetListAsync(
        Guid patientId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single allergy by its business key, or null when not found.
    /// </summary>
    Task<AllergyResponse?> GetByIdAsync(
        Guid patientId,
        Guid allergyId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the supplied <paramref name="drug"/> name conflicts with any
    /// active allergy for the patient using case-insensitive substring matching.
    /// Always returns a result (never null); <see cref="AllergyConflictResponse.HasConflict"/>
    /// is <c>false</c> when no conflict exists.
    /// </summary>
    Task<AllergyConflictResponse> CheckConflictAsync(
        Guid patientId,
        string drug,
        CancellationToken cancellationToken = default);

    /// <summary>Records a new active allergy against an existing patient.</summary>
    Task<AllergyCreateResult> CreateAsync(
        Guid patientId,
        CreateAllergyRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the mutable fields (allergen, severity, reaction, notes) of an allergy.
    /// Returns <see cref="AllergyUpdateNotFoundResult"/> when not found or soft-deleted.
    /// </summary>
    Task<AllergyUpdateResult> UpdateAsync(
        Guid patientId,
        Guid allergyId,
        UpdateAllergyRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions an active allergy to the Inactive state.
    /// Returns <see cref="AllergyDeactivateAlreadyInactiveResult"/> when already inactive.
    /// </summary>
    Task<AllergyDeactivateResult> DeactivateAsync(
        Guid patientId,
        Guid allergyId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes an allergy record. Returns false when not found.</summary>
    Task<bool> DeleteAsync(
        Guid patientId,
        Guid allergyId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);
}
