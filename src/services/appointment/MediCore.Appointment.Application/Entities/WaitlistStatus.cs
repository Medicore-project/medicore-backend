namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// Lifecycle status constants for <see cref="WaitlistEntry.Status"/>. String constants, as
/// <see cref="SlotStatus"/> and <see cref="AppointmentStatus"/> use.
/// </summary>
/// <remarks>
/// Only <see cref="Waiting"/> and <see cref="Offered"/> are active — they are what the
/// <c>ux_waitlist_entries_patient_active</c> index covers, so a patient can rejoin a day once an
/// earlier entry has closed. Every other status is terminal and kept as a record of how the entry
/// ended.
/// </remarks>
public static class WaitlistStatus
{
    /// <summary>In the queue, waiting for a slot on the day to be released.</summary>
    public const string Waiting = "Waiting";

    /// <summary>
    /// Holding a released slot until <see cref="WaitlistEntry.OfferExpiresAtUtc"/>. The slot is
    /// <see cref="SlotStatus.Offered"/> for as long as this lasts.
    /// </summary>
    public const string Offered = "Offered";

    /// <summary>The patient took the offer; <see cref="WaitlistEntry.AppointmentId"/> is set.</summary>
    public const string Accepted = "Accepted";

    /// <summary>The patient turned the offer down; the slot passed to the next entry.</summary>
    public const string Declined = "Declined";

    /// <summary>
    /// The offer lapsed unanswered, or the day passed while the entry was still waiting.
    /// </summary>
    public const string Expired = "Expired";

    /// <summary>
    /// Taken out of the queue: by the patient, by the front desk, or because the entry can no
    /// longer be served — the patient booked that day another way, or the clinic was cancelled.
    /// <see cref="WaitlistEntry.ClosedReason"/> says which.
    /// </summary>
    public const string Withdrawn = "Withdrawn";

    /// <summary>True for <see cref="Waiting"/> and <see cref="Offered"/>.</summary>
    public static bool IsActive(string? status) =>
        status is Waiting or Offered;
}
