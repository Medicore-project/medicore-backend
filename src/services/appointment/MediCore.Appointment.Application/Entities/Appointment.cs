namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// A confirmed booking of one <see cref="Slot"/> for one patient.
/// </summary>
/// <remarks>
/// <para>
/// Identity is <see cref="AppointmentId"/>, and at most one active appointment may exist per slot —
/// enforced by the partial unique index <c>ux_appointments_slot</c>. That index, not
/// <c>ux_slots_doctor_start</c>, is what actually prevents double-booking: booking <em>mutates</em>
/// a slot rather than inserting one, so the slot index can never fire. Two concurrent bookings both
/// read <see cref="SlotStatus.Available"/> and both write <see cref="SlotStatus.Booked"/>
/// successfully; only one of their appointment rows survives the insert.
/// </para>
/// <para>
/// The doctor and the time window are denormalised from the slot rather than joined back to it.
/// The patient-overlap check is a range predicate over this table alone, the row outlives a slot
/// that schedule revision hard-deletes, and the outbox event can be built from this entity without
/// a second read.
/// </para>
/// <para>
/// Cancelling an appointment is a <see cref="Status"/> transition to
/// <see cref="AppointmentStatus.Cancelled"/>, <strong>not</strong> a soft delete.
/// <see cref="IsDeleted"/> is administrative removal only. Wiring cancellation to the flag would
/// silently change which rows <c>ux_appointments_slot</c> covers.
/// </para>
/// </remarks>
public sealed class Appointment : IAuditableEntity
{
    /// <summary>Surrogate primary key — internal to the database.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Stable business key exposed on the API surface and carried by
    /// <c>AppointmentBookedEvent.AppointmentId</c>.
    /// </summary>
    public Guid AppointmentId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The booked slot's business key (<see cref="Slot.SlotId"/>). No foreign key: schedule
    /// revision may hard-delete a slot row, and the booking must survive that.
    /// </summary>
    public Guid SlotId { get; set; }

    /// <summary>
    /// The patient this visit is for — the <c>Id</c> the Patient service knows them by. No foreign
    /// key is possible: that row lives in another service's schema.
    /// </summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// The patient's number (<c>PAT-000042</c>) as it stood at booking time, so staff can see who
    /// booked without this service reaching into the Patient service. Copied from the signed
    /// booking token; null for a booking made by staff through the API with a bare
    /// <see cref="PatientId"/>, which carries nothing to copy.
    /// </summary>
    /// <remarks>
    /// A snapshot, not a reference: a later rename in the Patient service does not reach rows
    /// already written. Accepted — the alternative is a patient cache fed by patient-events, which
    /// is a consumer and a backfill for a display label.
    /// </remarks>
    public string? PatientNumber { get; set; }

    /// <summary>The patient's full name at booking time — see <see cref="PatientNumber"/>.</summary>
    public string? PatientName { get; set; }

    /// <summary>
    /// The doctor, denormalised from the slot. The <c>StaffId</c> from the Identity service, with
    /// no foreign key to <see cref="DoctorCache"/> for the same reason <see cref="Slot.DoctorId"/>
    /// has none — the cache is eventually consistent.
    /// </summary>
    public Guid DoctorId { get; set; }

    /// <summary>Appointment start as a UTC instant, denormalised from the slot.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Appointment end as a UTC instant, denormalised from the slot.</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>The Asia/Colombo calendar date, denormalised from the slot.</summary>
    public DateOnly SlotDate { get; set; }

    /// <summary>Slot length in minutes, copied from the slot at booking time.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>
    /// One of the <see cref="ServiceCodes"/> constants — what the billing service will invoice for.
    /// </summary>
    public string ServiceCode { get; set; } = ServiceCodes.GeneralConsultation;

    /// <summary>One of the <see cref="AppointmentStatus"/> constants.</summary>
    public string Status { get; set; } = AppointmentStatus.Booked;

    /// <summary>
    /// Soft-delete flag — true means the appointment is administratively removed. Not how a
    /// cancellation is recorded; see the remarks on this type.
    /// </summary>
    public bool IsDeleted { get; set; }

    // ── IAuditableEntity ──────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}
