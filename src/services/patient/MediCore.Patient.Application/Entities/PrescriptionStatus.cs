namespace MediCore.Patient.Application.Entities;

/// <summary>
/// Well-known string constants for <see cref="Prescription.Status"/>.
/// Using string literals (instead of a C# enum) keeps the column human-readable
/// in the database and avoids enum-migration noise — the same pattern used by
/// <c>ClinicalStatus</c> on <see cref="Condition"/>.
/// </summary>
public static class PrescriptionStatus
{
    /// <summary>The prescription is currently active and being administered.</summary>
    public const string Active = "Active";

    /// <summary>The prescription course has been completed and is now historical.</summary>
    public const string Completed = "Completed";
}
