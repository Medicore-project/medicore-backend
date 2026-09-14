namespace MediCore.Patient.Application.DTOs;

// ── Requests ──────────────────────────────────────────────────────────────────

/// <summary>Creates a new active prescription for a patient.</summary>
public sealed record CreatePrescriptionRequest(
    string Drug,
    string Dosage,
    string Frequency,
    int DurationDays,
    Guid? MedicalRecordId,
    string? Notes);

/// <summary>Updates the mutable fields of an active prescription.</summary>
public sealed record UpdatePrescriptionRequest(
    string Drug,
    string Dosage,
    string Frequency,
    int DurationDays,
    string? Notes);

// ── Responses ─────────────────────────────────────────────────────────────────

/// <summary>Full prescription detail returned by all read/write endpoints.</summary>
public sealed record PrescriptionResponse(
    Guid PrescriptionId,
    Guid PatientId,
    Guid? MedicalRecordId,
    string Drug,
    string Dosage,
    string Frequency,
    int DurationDays,
    string Status,
    string PrescriberClinicianId,
    string PrescriberClinicianEmail,
    string PrescriberClinicianRole,
    DateTime PrescribedAtUtc,
    DateTime? CompletedAtUtc,
    string? Notes);

/// <summary>
/// Split list returned by GET /prescriptions — active items are immediately
/// visible; history is a collapsible archive of completed prescriptions.
/// </summary>
public sealed record PrescriptionListResponse(
    IReadOnlyList<PrescriptionResponse> Active,
    IReadOnlyList<PrescriptionResponse> History);
