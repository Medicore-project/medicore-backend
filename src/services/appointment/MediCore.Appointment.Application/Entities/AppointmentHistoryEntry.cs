namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// One change to one appointment: who did what, when, and from what to what.
/// </summary>
/// <remarks>
/// <para>
/// SCRUM-36 asks for "the appointment history records every change". A row is written in the same
/// save as the change it describes, so the history can never disagree with the appointment: both
/// commit or neither does.
/// </para>
/// <para>
/// Append-only, like <see cref="OutboxMessage"/>, so this entity deliberately has no
/// <c>IsDeleted</c> flag and no <see cref="IAuditableEntity"/> columns. The row <em>is</em> the
/// audit record; nothing updates or removes it.
/// </para>
/// <para>
/// A completion's clinical notes are not copied here. They travel to the Patient service on
/// <c>appointment.completed</c> and become part of the medical record there, which is where
/// clinical text belongs; this table is visible to every clinic role.
/// </para>
/// </remarks>
public sealed class AppointmentHistoryEntry
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The appointment's business key (<see cref="Appointment.AppointmentId"/>).</summary>
    public Guid AppointmentId { get; set; }

    /// <summary>One of the <see cref="AppointmentHistoryAction"/> constants.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The status before the change; null for the booking that created the appointment.</summary>
    public string? FromStatus { get; set; }

    /// <summary>The status after the change.</summary>
    public string ToStatus { get; set; } = string.Empty;

    /// <summary>The slot before the change; null for the booking.</summary>
    public Guid? FromSlotId { get; set; }

    /// <summary>The slot after the change.</summary>
    public Guid? ToSlotId { get; set; }

    /// <summary>The start time before the change; null for the booking.</summary>
    public DateTime? FromStartUtc { get; set; }

    /// <summary>The start time after the change.</summary>
    public DateTime? ToStartUtc { get; set; }

    /// <summary>Why, when the caller gave a reason — a cancellation's reason, for instance.</summary>
    public string? Reason { get; set; }

    /// <summary>Who made the change, as the controllers' <c>CurrentActor()</c> names them.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>When the change was made, from the service's <see cref="TimeProvider"/>.</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// The entry a new booking starts its history with.
    /// </summary>
    public static AppointmentHistoryEntry ForBooking(
        Appointment appointment,
        string actor,
        DateTime occurredAtUtc) => new()
    {
        AppointmentId = appointment.AppointmentId,
        Action = AppointmentHistoryAction.Booked,
        ToStatus = appointment.Status,
        ToSlotId = appointment.SlotId,
        ToStartUtc = appointment.StartUtc,
        Actor = actor,
        OccurredAtUtc = occurredAtUtc
    };
}

/// <summary>What an <see cref="AppointmentHistoryEntry"/> records.</summary>
public static class AppointmentHistoryAction
{
    public const string Booked = "Booked";
    public const string Rescheduled = "Rescheduled";
    public const string Cancelled = "Cancelled";
    public const string Completed = "Completed";
}
