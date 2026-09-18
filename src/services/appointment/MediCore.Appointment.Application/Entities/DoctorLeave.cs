namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// A period during which one doctor is unavailable. Inclusive of both end dates, and unlike a
/// <see cref="PublicHoliday"/> it affects only the named doctor.
/// </summary>
public sealed class DoctorLeave : IAuditableEntity
{
    /// <summary>Surrogate primary key — internal to the database.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable business key exposed on the API surface.</summary>
    public Guid LeaveId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The doctor on leave — the <c>StaffId</c> from the Identity service.
    /// Deliberately stored without a foreign key: the DoctorCache table arrives in SCRUM-33.
    /// </summary>
    public Guid DoctorId { get; set; }

    /// <summary>First day of leave, inclusive (Asia/Colombo calendar).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Last day of leave, inclusive (Asia/Colombo calendar).</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>Optional free-text reason.</summary>
    public string? Reason { get; set; }

    /// <summary>Soft-delete flag — true means the leave record is logically removed.</summary>
    public bool IsDeleted { get; set; }

    // ── IAuditableEntity ──────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}
