namespace MediCore.Patient.Application.Entities;

/// <summary>
/// Represents a medication prescription issued to a patient by a clinician.
/// Status transitions from <see cref="PrescriptionStatus.Active"/> to
/// <see cref="PrescriptionStatus.Completed"/> when the prescriber marks it done;
/// completed prescriptions are retained as a read-only history.
/// </summary>
public sealed class Prescription : IAuditableEntity
{
    /// <summary>Surrogate primary key — internal to the database.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Stable business key exposed on the API surface.
    /// Mirrors the <c>RecordId</c> pattern used by <see cref="MedicalRecord"/>.
    /// </summary>
    public Guid PrescriptionId { get; set; } = Guid.NewGuid();

    /// <summary>Patient this prescription belongs to.</summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// Optional reference to the medical-record visit that prompted this prescription.
    /// Null when the prescription is created outside of a visit context.
    /// </summary>
    public Guid? MedicalRecordId { get; set; }

    /// <summary>Name of the prescribed drug / medication.</summary>
    public string Drug { get; set; } = string.Empty;

    /// <summary>Dosage per administration (e.g. "500 mg", "10 mL").</summary>
    public string Dosage { get; set; } = string.Empty;

    /// <summary>How often the medication is taken (e.g. "Twice daily", "Every 8 hours").</summary>
    public string Frequency { get; set; } = string.Empty;

    /// <summary>Intended course duration in days.</summary>
    public int DurationDays { get; set; }

    /// <summary>Current lifecycle status: <c>"Active"</c> or <c>"Completed"</c>.</summary>
    public string Status { get; set; } = PrescriptionStatus.Active;

    /// <summary>UTC timestamp when the prescription was marked complete. Null while active.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    // ── Prescriber identity (denormalised from the JWT at creation time) ──────

    public string PrescriberClinicianId { get; set; } = string.Empty;
    public string PrescriberClinicianEmail { get; set; } = string.Empty;
    public string PrescriberClinicianRole { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the prescription was originally written.</summary>
    public DateTime PrescribedAtUtc { get; set; }

    /// <summary>Optional free-text notes from the prescriber.</summary>
    public string? Notes { get; set; }

    /// <summary>Soft-delete flag — true means the prescription is logically removed.</summary>
    public bool IsDeleted { get; set; }

    // ── IAuditableEntity ──────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}
