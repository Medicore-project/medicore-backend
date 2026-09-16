namespace MediCore.Patient.Application.Entities;

/// <summary>
/// Represents a known patient allergy recorded by a clinician.
/// Active allergies are checked against new prescriptions at the point of care
/// to prevent dangerous drug interactions.
/// </summary>
public sealed class Allergy : IAuditableEntity
{
    /// <summary>Surrogate primary key — internal to the database.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Stable business key exposed on the API surface.
    /// Mirrors the <c>PrescriptionId</c> pattern used by <see cref="Prescription"/>.
    /// </summary>
    public Guid AllergyId { get; set; } = Guid.NewGuid();

    /// <summary>Patient this allergy belongs to.</summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// Name of the allergen (e.g. "Penicillin", "Amoxicillin", "Latex").
    /// Used for case-insensitive substring matching against new prescription drug names.
    /// </summary>
    public string Allergen { get; set; } = string.Empty;

    /// <summary>
    /// Clinical severity of the allergy reaction.
    /// Allowed values: <c>"Mild"</c>, <c>"Moderate"</c>, <c>"Severe"</c>, <c>"Unknown"</c>.
    /// </summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the observed reaction (e.g. "Anaphylaxis", "Rash").
    /// Null when no reaction description has been recorded.
    /// </summary>
    public string? Reaction { get; set; }

    /// <summary>
    /// Current lifecycle status: <c>"Active"</c> or <c>"Inactive"</c>.
    /// Only active allergies participate in prescription conflict checks.
    /// </summary>
    public string Status { get; set; } = AllergyStatus.Active;

    /// <summary>UTC timestamp when the allergy was first recorded.</summary>
    public DateTime RecordedAtUtc { get; set; }

    /// <summary>Optional free-text clinical notes from the recording clinician.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Clinician who recorded this allergy (denormalised from the JWT at creation time).
    /// </summary>
    public string RecordedByClinicianId { get; set; } = string.Empty;

    /// <summary>Email of the recording clinician (denormalised from the JWT).</summary>
    public string RecordedByClinicianEmail { get; set; } = string.Empty;

    /// <summary>Role of the recording clinician (denormalised from the JWT).</summary>
    public string RecordedByClinicianRole { get; set; } = string.Empty;

    /// <summary>Soft-delete flag — true means the allergy is logically removed.</summary>
    public bool IsDeleted { get; set; }

    // ── IAuditableEntity ──────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}
