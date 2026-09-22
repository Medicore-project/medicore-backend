namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// This service's own copy of one doctor's details, kept current from Identity's staff-events.
/// </summary>
/// <remarks>
/// Deliberate eventual consistency: Identity owns staff data, and this is a read model of it, so
/// listing and validating doctors keeps working while Identity is down. The price is a short lag —
/// a change made in Identity is visible here only once its event has been consumed.
/// </remarks>
public sealed class DoctorCache : IAuditableEntity
{
    /// <summary>Surrogate primary key — internal to the database.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The doctor's <c>StaffId</c> in the Identity service — the same Guid that
    /// <see cref="DoctorSchedule.DoctorId"/>, <see cref="Slot.DoctorId"/> and
    /// <see cref="DoctorLeave.DoctorId"/> hold, and the business key exposed on the API.
    /// </summary>
    public Guid DoctorId { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>Free-text specialization name as held by Identity. Empty when none is set.</summary>
    public string Specialization { get; set; } = string.Empty;

    public Guid DepartmentId { get; set; }

    /// <summary>
    /// Whether the doctor can currently be booked. False after deactivation or losing the Doctor
    /// role. The row is kept rather than deleted so existing schedules and slots still resolve a name.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// <c>OccurredAtUtc</c> of the newest event applied to this row. An event older than this is
    /// stale — a redelivery or replay overtaken by later changes — and must not roll the row back.
    /// </summary>
    public DateTime LastEventOccurredAtUtc { get; set; }

    /// <summary>Soft-delete flag — true means the row is logically removed.</summary>
    public bool IsDeleted { get; set; }

    // ── IAuditableEntity ──────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}
