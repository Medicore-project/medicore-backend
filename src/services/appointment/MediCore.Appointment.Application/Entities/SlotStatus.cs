namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// Lifecycle status constants for <see cref="Slot.Status"/>.
/// Using string constants (not a C# enum) keeps the column human-readable in the database
/// and avoids enum-migration noise — the same pattern the Patient service uses for
/// prescription and allergy status.
/// </summary>
public static class SlotStatus
{
    /// <summary>Generated and bookable. The status every slot starts in.</summary>
    public const string Available = "Available";

    /// <summary>
    /// Reserved by an appointment. Nothing in SCRUM-32 writes this value — booking arrives in
    /// SCRUM-34 — but slot generation and schedule revision must already honour it, so tests
    /// set it directly.
    /// </summary>
    public const string Booked = "Booked";

    /// <summary>
    /// Deliberately suppressed by an administrator without deleting the row. Never set by slot
    /// generation; only by an explicit block request.
    /// </summary>
    public const string Blocked = "Blocked";

    /// <summary>
    /// Was <see cref="Booked"/> but now falls outside the doctor's schedule — because the schedule
    /// changed, or the date became a public holiday or leave day. Retained rather than deleted so
    /// the booking is never silently lost (SCRUM-32 AC2); surfaces in the "needs attention" list
    /// for a receptionist to reschedule.
    /// </summary>
    public const string Flagged = "Flagged";
}
