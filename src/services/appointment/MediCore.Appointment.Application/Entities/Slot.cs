namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// A single bookable time window for one doctor, produced by slot generation from a
/// <see cref="DoctorSchedule"/>.
/// </summary>
/// <remarks>
/// Identity is <c>(DoctorId, StartUtc)</c>, enforced by the partial unique index
/// <c>ux_slots_doctor_start</c>. That index deliberately excludes
/// <see cref="SlotStatus.Flagged"/> rows: a flagged slot is a historical record of a booking that
/// no longer fits the schedule, so a fresh bookable slot may legitimately exist at the same
/// instant once the schedule covers that time again.
/// </remarks>
public sealed class Slot : IAuditableEntity
{
    /// <summary>Surrogate primary key — internal to the database.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable business key exposed on the API surface.</summary>
    public Guid SlotId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The doctor this slot belongs to — the <c>StaffId</c> from the Identity service.
    /// Deliberately no foreign key to <see cref="DoctorCache"/>: that cache is eventually consistent,
    /// and a hard link would make writes here depend on a staff event having been consumed first.
    /// </summary>
    public Guid DoctorId { get; set; }

    /// <summary>
    /// The schedule that generated this slot. Null when the schedule has since been removed;
    /// the slot itself is retained so bookings survive.
    /// </summary>
    public Guid? DoctorScheduleId { get; set; }

    /// <summary>Slot start as a UTC instant.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Slot end as a UTC instant, always <see cref="DurationMinutes"/> after the start.</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>
    /// The Asia/Colombo calendar date this slot falls on. Denormalised from <see cref="StartUtc"/>
    /// so that holiday and leave matching is a plain date equality rather than a range comparison
    /// across a timezone offset.
    /// </summary>
    public DateOnly SlotDate { get; set; }

    /// <summary>Slot length in minutes, copied from the schedule at generation time.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>One of the <see cref="SlotStatus"/> constants.</summary>
    public string Status { get; set; } = SlotStatus.Available;

    /// <summary>Why the slot was flagged. Null unless <see cref="Status"/> is Flagged.</summary>
    public string? FlaggedReason { get; set; }

    /// <summary>When the slot was flagged. Null unless <see cref="Status"/> is Flagged.</summary>
    public DateTime? FlaggedAtUtc { get; set; }

    /// <summary>Soft-delete flag — true means the slot is logically removed.</summary>
    public bool IsDeleted { get; set; }

    // ── IAuditableEntity ──────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}
