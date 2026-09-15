namespace MediCore.Patient.Application.DTOs;

// ── Requests ──────────────────────────────────────────────────────────────────

/// <summary>Records a new allergy against a patient.</summary>
public sealed record CreateAllergyRequest(
    string Allergen,
    string Severity,
    string? Reaction,
    string? Notes);

/// <summary>
/// Updates the mutable fields of an existing allergy record.
/// Status transitions are handled by the dedicated
/// <c>PATCH .../deactivate</c> endpoint.
/// </summary>
public sealed record UpdateAllergyRequest(
    string Allergen,
    string Severity,
    string? Reaction,
    string? Notes);

// ── Responses ─────────────────────────────────────────────────────────────────

/// <summary>Full allergy detail returned by all read / write endpoints.</summary>
public sealed record AllergyResponse(
    Guid AllergyId,
    Guid PatientId,
    string Allergen,
    string Severity,
    string? Reaction,
    string Status,
    DateTime RecordedAtUtc,
    string? Notes,
    string RecordedByClinicianId,
    string RecordedByClinicianEmail,
    string RecordedByClinicianRole);

/// <summary>
/// Returned by <c>GET .../allergies/check?drug=</c> and embedded inside
/// <see cref="PrescriptionCreateAllergyConflictResult"/> when a conflict is detected.
/// </summary>
public sealed record AllergyConflictResponse(
    bool HasConflict,
    AllergyResponse? MatchedAllergy);
