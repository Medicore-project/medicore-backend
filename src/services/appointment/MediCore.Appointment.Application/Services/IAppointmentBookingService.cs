using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Services;

// ── Discriminated union results ───────────────────────────────────────────────

public abstract record BookingResult;

/// <summary>The slot is taken, the appointment exists, and the event row is waiting in the outbox.</summary>
public sealed record BookingCreatedResult(AppointmentResponse Appointment) : BookingResult;

/// <summary>No slot with that business key. The controller maps this to 404.</summary>
public sealed record BookingSlotNotFoundResult : BookingResult;

/// <summary>
/// The slot's doctor is not in the doctor cache, or is there but no longer bookable. SCRUM-33
/// leaves a deactivated doctor's slot rows in place on purpose, so without this a patient could
/// book someone who has left the clinic. The controller maps this to 404.
/// </summary>
public sealed record BookingDoctorNotFoundResult : BookingResult;

/// <summary>
/// The slot exists but is not free — already booked, blocked, or flagged. The controller maps
/// this to 409 and names the status.
/// </summary>
public sealed record BookingSlotNotAvailableResult(string CurrentStatus) : BookingResult;

/// <summary>
/// The slot's start time has passed. SCRUM-34 AC2 — the controller maps this to 400.
/// </summary>
public sealed record BookingSlotInPastResult(DateTime StartUtc) : BookingResult;

/// <summary>
/// The patient already has a booking overlapping this slot. SCRUM-34 AC3 — the controller maps
/// this to 409 and describes the existing appointment so the clash can be explained.
/// </summary>
public sealed record BookingPatientOverlapResult(
    Guid ExistingAppointmentId,
    DateTime ExistingStartUtc,
    DateTime ExistingEndUtc) : BookingResult;

/// <summary>
/// Someone else booked this slot between the availability check and the insert. The controller
/// maps this to 409.
/// </summary>
public sealed record BookingSlotTakenResult : BookingResult;

/// <summary>
/// SCRUM-35. Every attempt lost a concurrency race on the slot and the retries ran out, so the
/// service cannot say what the slot's state will settle at. The controller maps this to 409 and
/// asks the caller to choose again — never a 500, and never a guess that it was booked.
/// </summary>
public sealed record BookingContendedResult : BookingResult;

// ── Service contract ──────────────────────────────────────────────────────────

/// <summary>Booking a generated slot for a patient.</summary>
public interface IAppointmentBookingService
{
    /// <summary>
    /// Takes a free future slot for a patient, writing the appointment, the slot's new status and
    /// the <c>appointment.booked</c> outbox row in one transaction.
    /// </summary>
    /// <param name="patientId">
    /// Resolved by the controller — from the caller's <c>patientId</c> claim when they present a
    /// booking token, otherwise from the request body. Passed separately rather than read off the
    /// request so that "whose appointment is this" is decided in exactly one place.
    /// </param>
    /// <param name="serviceCode">
    /// One of the <c>ServiceCodes</c> constants, or null for a general consultation. Already
    /// validated by the time it reaches here.
    /// </param>
    /// <param name="correlationId">Carried onto the outbox row and the Kafka header.</param>
    /// <param name="patientDetails">
    /// The patient's number and name, recorded on the appointment for display. Resolved by the
    /// controller alongside <paramref name="patientId"/>, from the same booking token; null when
    /// there is nothing trustworthy to record.
    /// </param>
    Task<BookingResult> BookAsync(
        Guid slotId,
        Guid patientId,
        string? serviceCode,
        string actor,
        string correlationId,
        BookingPatientDetails? patientDetails = null,
        CancellationToken cancellationToken = default);

    /// <summary>One appointment by its business key, or null if there is none.</summary>
    Task<AppointmentResponse?> GetByIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);
}
