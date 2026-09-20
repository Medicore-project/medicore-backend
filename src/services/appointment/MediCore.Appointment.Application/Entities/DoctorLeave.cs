namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// A request for a period during which one doctor is unavailable. Inclusive of both end dates,
/// and unlike a <see cref="PublicHoliday"/> it affects only the named doctor.
/// </summary>
/// <remarks>
/// Leave is requested, not taken: a new record starts at <see cref="LeaveStatus.Pending"/> and an
/// administrator must approve it before it affects anything. Slot generation honours only
/// <see cref="LeaveStatus.Approved"/> records, so a pending or rejected request leaves the
/// doctor's calendar untouched.
/// <para>
/// Withdrawal is a soft delete rather than a fourth status, matching the soft-delete convention
/// used across the service.
/// </para>
/// </remarks>
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

    /// <summary>Optional free-text reason supplied by the requester.</summary>
    public string? Reason { get; set; }

    // ── Approval workflow ─────────────────────────────────────────────────────

    /// <summary>
    /// One of the <see cref="LeaveStatus"/> constants. Only <see cref="LeaveStatus.Approved"/>
    /// suppresses slot generation.
    /// </summary>
    public string Status { get; set; } = LeaveStatus.Pending;

    /// <summary>
    /// The administrator who approved or rejected the request. Null while still pending.
    /// </summary>
    public string? ReviewedBy { get; set; }

    /// <summary>When the decision was made. Null while still pending.</summary>
    public DateTime? ReviewedAtUtc { get; set; }

    /// <summary>
    /// Free-text note attached to the decision — typically why a request was rejected.
    /// </summary>
    public string? ReviewNotes { get; set; }

    /// <summary>Soft-delete flag — true means the leave record is logically removed.</summary>
    public bool IsDeleted { get; set; }

    // ── IAuditableEntity ──────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}
