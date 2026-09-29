namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// Lifecycle status constants for <see cref="Appointment.Status"/>.
/// String constants (not a C# enum) keep the column human-readable in the database and avoid
/// enum-migration noise — the same pattern <see cref="SlotStatus"/> and
/// <see cref="LeaveStatus"/> already use.
/// </summary>
public static class AppointmentStatus
{
    /// <summary>Confirmed and upcoming. The only value SCRUM-34 ever writes.</summary>
    public const string Booked = "Booked";

    /// <summary>
    /// Called off before it happened, releasing the slot. Nothing in SCRUM-34 writes this value —
    /// cancellation arrives with <c>appointment.cancelled</c> in a later ticket — but the partial
    /// unique index <c>ux_appointments_slot</c> already excludes it, so the vocabulary has to exist.
    /// </summary>
    public const string Cancelled = "Cancelled";

    /// <summary>
    /// The visit happened. Nothing in SCRUM-34 writes this value; the Patient service already
    /// consumes an <c>appointment.completed</c> event that a later ticket will produce.
    /// </summary>
    public const string Completed = "Completed";

    /// <summary>
    /// The patient did not attend. Nothing in SCRUM-34 writes this value. Deliberately distinct
    /// from <see cref="Cancelled"/>: the slot was consumed, so it is not released.
    /// </summary>
    public const string NoShow = "NoShow";
}
