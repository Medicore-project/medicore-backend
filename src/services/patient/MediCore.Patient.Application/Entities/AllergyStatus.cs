namespace MediCore.Patient.Application.Entities;

/// <summary>
/// Lifecycle status constants for the <see cref="Allergy"/> entity.
/// Using string constants (not an enum) mirrors the <see cref="PrescriptionStatus"/>
/// pattern and avoids enum-to-column mapping in EF Core migrations.
/// </summary>
public static class AllergyStatus
{
    /// <summary>
    /// The allergy is currently active and must be checked at the point of prescribing.
    /// This is the default status when a new allergy is recorded.
    /// </summary>
    public const string Active = "Active";

    /// <summary>
    /// The allergy has been marked inactive (e.g. resolved, misdiagnosed).
    /// Inactive allergies are retained for the audit trail but are excluded
    /// from conflict checks during prescription creation.
    /// </summary>
    public const string Inactive = "Inactive";
}
