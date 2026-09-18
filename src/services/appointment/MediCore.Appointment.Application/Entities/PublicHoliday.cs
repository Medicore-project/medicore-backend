namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// A clinic-wide non-working day. Applies to every doctor, so no slots are generated for this
/// date for anyone (SCRUM-32 AC3).
/// </summary>
public sealed class PublicHoliday : IAuditableEntity
{
    /// <summary>Surrogate primary key — internal to the database.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable business key exposed on the API surface.</summary>
    public Guid HolidayId { get; set; } = Guid.NewGuid();

    /// <summary>The Asia/Colombo calendar date the clinic is closed.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Display name, for example "Vesak Full Moon Poya Day".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Soft-delete flag — true means the holiday is logically removed.</summary>
    public bool IsDeleted { get; set; }

    // ── IAuditableEntity ──────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}
