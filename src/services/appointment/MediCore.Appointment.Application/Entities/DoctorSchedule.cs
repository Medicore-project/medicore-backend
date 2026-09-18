namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// One doctor's recurring working window for a single day of the week.
/// A "weekly schedule" as seen in the UI is up to seven of these rows.
/// </summary>
/// <remarks>
/// Modelling one row per weekday (rather than a set of days on one row) keeps the AC4 overlap
/// check simple and lets a doctor work different hours on different days. It also supports split
/// shifts for free: 09:00-12:00 and 13:00-17:00 on the same day do not overlap, so a lunch break
/// needs no additional concept.
/// </remarks>
public sealed class DoctorSchedule : IAuditableEntity
{
    /// <summary>Surrogate primary key — internal to the database.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable business key exposed on the API surface.</summary>
    public Guid ScheduleId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The doctor this schedule belongs to — the <c>StaffId</c> from the Identity service.
    /// Deliberately stored without a foreign key: the DoctorCache table arrives in SCRUM-33.
    /// </summary>
    public Guid DoctorId { get; set; }

    /// <summary>Day of the week this window applies to. Stored as an integer (Sunday = 0).</summary>
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Start of the working window, as Asia/Colombo wall-clock time.</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>End of the working window, as Asia/Colombo wall-clock time. Must be after <see cref="StartTime"/>.</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>Length of each generated slot. Constrained to 15 or 30 minutes by validation.</summary>
    public int SlotDurationMinutes { get; set; }

    /// <summary>First date (inclusive, Colombo calendar) this schedule produces slots for.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>
    /// Last date (inclusive, Colombo calendar) this schedule produces slots for.
    /// Null means open-ended.
    /// </summary>
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>
    /// Whether the schedule currently generates slots. Lets an administrator pause a schedule
    /// without deleting it and losing the overlap history.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Soft-delete flag — true means the schedule is logically removed.</summary>
    public bool IsDeleted { get; set; }

    // ── IAuditableEntity ──────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}
