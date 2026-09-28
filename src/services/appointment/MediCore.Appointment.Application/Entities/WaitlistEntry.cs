namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// One patient's place in the queue for a full day with one doctor (SCRUM-37).
/// </summary>
/// <remarks>
/// <para>
/// A queue is a (<see cref="DoctorId"/>, <see cref="SlotDate"/>) pair: slots belong to a doctor
/// and a Colombo date, and there is no clinic entity above them.
/// </para>
/// <para>
/// <see cref="Position"/> is stored once, at join, as one more than the highest position the queue
/// has ever used, and never changes. The place in line a patient is shown is derived from it — the
/// number of <see cref="WaitlistStatus.Waiting"/> entries with a lower position, plus one — so an
/// entry leaving never rewrites the rows behind it.
/// </para>
/// <para>
/// Accepting an offer "clears" the entry by moving it to <see cref="WaitlistStatus.Accepted"/>,
/// not by soft-deleting it: <see cref="IsDeleted"/> is administrative removal, as on
/// <see cref="Appointment"/>, and the closed row is the record of how the patient got their time.
/// </para>
/// </remarks>
public sealed class WaitlistEntry : IAuditableEntity
{
    /// <summary>Surrogate primary key — internal to the database.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable business key exposed on the API surface.</summary>
    public Guid WaitlistEntryId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The doctor whose day is full. No foreign key to <see cref="DoctorCache"/>, as on
    /// <see cref="Slot.DoctorId"/>.
    /// </summary>
    public Guid DoctorId { get; set; }

    /// <summary>The Asia/Colombo calendar date the patient is waiting for.</summary>
    public DateOnly SlotDate { get; set; }

    /// <summary>The patient, as the Patient service knows them. No foreign key is possible.</summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// The patient's number at join time, copied from the booking token as
    /// <see cref="Appointment.PatientNumber"/> is. Carried onto the appointment an accepted offer
    /// creates, so a front-desk acceptance — whose staff token has no patient claims — still
    /// records who it is for.
    /// </summary>
    public string? PatientNumber { get; set; }

    /// <summary>The patient's full name at join time — see <see cref="PatientNumber"/>.</summary>
    public string? PatientName { get; set; }

    /// <summary>
    /// What the appointment will be billed as, chosen at join. One of the <see cref="ServiceCodes"/>
    /// constants, so an accepted offer is invoiced exactly as a booking would have been.
    /// </summary>
    public string ServiceCode { get; set; } = ServiceCodes.GeneralConsultation;

    /// <summary>The entry's fixed place in the queue's order. See the remarks on this type.</summary>
    public int Position { get; set; }

    /// <summary>One of the <see cref="WaitlistStatus"/> constants.</summary>
    public string Status { get; set; } = WaitlistStatus.Waiting;

    /// <summary>When the patient joined.</summary>
    public DateTime JoinedAtUtc { get; set; }

    /// <summary>
    /// The held slot's business key (<see cref="Slot.SlotId"/>) while <see cref="Status"/> is
    /// <see cref="WaitlistStatus.Offered"/>; kept afterwards as a record of what was offered. No
    /// foreign key: schedule revision may hard-delete the slot row.
    /// </summary>
    public Guid? OfferedSlotId { get; set; }

    /// <summary>When the current (or last) offer was made.</summary>
    public DateTime? OfferedAtUtc { get; set; }

    /// <summary>
    /// When the offer lapses: the offer window after it was made, but never later than the slot's
    /// start. An accept at or after this instant is refused whether or not the sweeper has run.
    /// </summary>
    public DateTime? OfferExpiresAtUtc { get; set; }

    /// <summary>The appointment an accepted offer created.</summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>When the entry reached a terminal status.</summary>
    public DateTime? ClosedAtUtc { get; set; }

    /// <summary>Why a withdrawn entry was taken out of the queue, when there is something to say.</summary>
    public string? ClosedReason { get; set; }

    /// <summary>Soft-delete flag — administrative removal only. See the remarks on this type.</summary>
    public bool IsDeleted { get; set; }

    // ── IAuditableEntity ──────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}
