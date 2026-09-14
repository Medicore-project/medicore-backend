using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Services;

// ── Discriminated union results ───────────────────────────────────────────────
// Same pattern used by MedicalRecordCreateResult / MedicalRecordUpdateResult

public abstract record PrescriptionCreateResult;
public sealed record PrescriptionCreatedResult(PrescriptionResponse Prescription) : PrescriptionCreateResult;
public sealed record PrescriptionCreatePatientNotFoundResult : PrescriptionCreateResult;

public abstract record PrescriptionUpdateResult;
public sealed record PrescriptionUpdatedResult(PrescriptionResponse Prescription) : PrescriptionUpdateResult;
public sealed record PrescriptionUpdateNotFoundResult : PrescriptionUpdateResult;

public abstract record PrescriptionCompleteResult;
public sealed record PrescriptionCompletedResult(PrescriptionResponse Prescription) : PrescriptionCompleteResult;
public sealed record PrescriptionCompleteNotFoundResult : PrescriptionCompleteResult;

/// <summary>
/// Returned when <c>PATCH .../complete</c> is called on a prescription
/// that is already in the <c>Completed</c> state.
/// </summary>
public sealed record PrescriptionCompleteAlreadyDoneResult : PrescriptionCompleteResult;

// ── Service contract ──────────────────────────────────────────────────────────

/// <summary>
/// Application-layer service for the Prescription aggregate.
/// Owns all business logic: creation, field updates, status transition
/// (Active → Completed), soft-delete, and audit trail.
/// </summary>
public interface IPrescriptionService
{
    /// <summary>
    /// Returns all prescriptions for a patient split into active and history lists.
    /// Returns null when the patient does not exist.
    /// </summary>
    Task<PrescriptionListResponse?> GetListAsync(
        Guid patientId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single prescription by its business key.
    /// Returns null when not found or soft-deleted.
    /// </summary>
    Task<PrescriptionResponse?> GetByIdAsync(
        Guid patientId,
        Guid prescriptionId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new active prescription for an existing patient.</summary>
    Task<PrescriptionCreateResult> CreateAsync(
        Guid patientId,
        CreatePrescriptionRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the mutable fields of an active prescription.
    /// Returns <see cref="PrescriptionUpdateNotFoundResult"/> when not found
    /// or when the prescription is already completed.
    /// </summary>
    Task<PrescriptionUpdateResult> UpdateAsync(
        Guid patientId,
        Guid prescriptionId,
        UpdatePrescriptionRequest request,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions an active prescription to the Completed state (moves it to history).
    /// Returns <see cref="PrescriptionCompleteAlreadyDoneResult"/> when already completed.
    /// </summary>
    Task<PrescriptionCompleteResult> CompleteAsync(
        Guid patientId,
        Guid prescriptionId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a prescription. Returns false when not found.</summary>
    Task<bool> DeleteAsync(
        Guid patientId,
        Guid prescriptionId,
        PatientAccessContext accessContext,
        CancellationToken cancellationToken = default);
}
