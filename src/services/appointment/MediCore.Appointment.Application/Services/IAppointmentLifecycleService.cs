using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Services;

/// <summary>
/// Who is changing an appointment.
/// </summary>
/// <param name="Actor">The name recorded in the history and the audit columns.</param>
/// <param name="PatientId">
/// Set for a booking-token caller, who may only change their own appointments. Null for staff.
/// </param>
/// <param name="StaffId">The caller's staff id, when they have one.</param>
public sealed record AppointmentCaller(string Actor, Guid? PatientId = null, Guid? StaffId = null);

/// <summary>The outcome of a change to an existing appointment. The controller picks the status code.</summary>
public abstract record AppointmentChangeResult;

/// <summary>The change was made and committed.</summary>
public sealed record AppointmentChangedResult(AppointmentResponse Appointment) : AppointmentChangeResult;

/// <summary>
/// No such appointment — or, for a patient caller, not theirs. The two answers are identical on
/// purpose, so a booking token cannot probe which appointment ids exist.
/// </summary>
public sealed record AppointmentNotFoundResult : AppointmentChangeResult;

/// <summary>
/// The appointment's current status does not allow this change — cancelling a completed visit,
/// say. <paramref name="Action"/> is the <c>AppointmentHistoryAction</c> that was refused.
/// </summary>
public sealed record AppointmentInvalidTransitionResult(string CurrentStatus, string Action)
    : AppointmentChangeResult;

/// <summary>
/// Too close to the start to cancel or reschedule, under the configured policy. With a zero window
/// this means the appointment has already started.
/// </summary>
public sealed record AppointmentInsideCancellationWindowResult(int WindowHours, DateTime StartUtc)
    : AppointmentChangeResult;

/// <summary>Three attempts in a row lost a race on the slot row; the caller is asked to try again.</summary>
public sealed record AppointmentContendedResult : AppointmentChangeResult;

/// <summary>The slot a reschedule asked for does not exist.</summary>
public sealed record AppointmentNewSlotNotFoundResult : AppointmentChangeResult;

/// <summary>The slot a reschedule asked for is the one the appointment already has.</summary>
public sealed record AppointmentNewSlotSameAsCurrentResult : AppointmentChangeResult;

/// <summary>
/// The slot a reschedule asked for belongs to a different doctor. Rescheduling moves the time, not
/// the doctor; changing doctor is a cancellation and a new booking.
/// </summary>
public sealed record AppointmentNewSlotDifferentDoctorResult : AppointmentChangeResult;

/// <summary>The appointment's doctor is no longer bookable, so nothing can be moved to them.</summary>
public sealed record AppointmentDoctorNotFoundResult : AppointmentChangeResult;

/// <summary>The slot a reschedule asked for is not <c>Available</c>.</summary>
public sealed record AppointmentNewSlotNotAvailableResult(string CurrentStatus) : AppointmentChangeResult;

/// <summary>The slot a reschedule asked for has already started.</summary>
public sealed record AppointmentNewSlotInPastResult(DateTime StartUtc) : AppointmentChangeResult;

/// <summary>The patient has another booked appointment overlapping the new time.</summary>
public sealed record AppointmentPatientOverlapResult(
    Guid ExistingAppointmentId,
    DateTime ExistingStartUtc,
    DateTime ExistingEndUtc) : AppointmentChangeResult;

/// <summary>
/// Only the appointment's own doctor may complete it, and the caller is not that doctor — or has
/// no staff id at all.
/// </summary>
public sealed record AppointmentNotYourAppointmentResult : AppointmentChangeResult;

/// <summary>The appointment has not started yet, so it cannot have been completed.</summary>
public sealed record AppointmentNotStartedYetResult(DateTime StartUtc) : AppointmentChangeResult;

/// <summary>
/// Someone else booked the slot a reschedule asked for while it was in flight. The reschedule
/// rolled back, so the appointment is still on its original slot.
/// </summary>
public sealed record AppointmentSlotTakenResult : AppointmentChangeResult;

/// <summary>Reschedules, cancels and completes existing appointments.</summary>
/// <remarks>
/// Separate from <see cref="IAppointmentBookingService"/>, which only ever creates. Each change
/// runs in one transaction that locks the appointment row first, writes the appointment, any slot
/// change, a history entry and any outbox row together, and commits all of them or none.
/// </remarks>
public interface IAppointmentLifecycleService
{
    /// <summary>
    /// Cancels a booked appointment outside the cancellation window, releasing its slot and
    /// announcing <c>appointment.cancelled</c>.
    /// </summary>
    Task<AppointmentChangeResult> CancelAsync(
        Guid appointmentId,
        string reason,
        AppointmentCaller caller,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a booked appointment to another available slot with the same doctor, releasing the
    /// old slot and taking the new one in a single transaction. If the new slot is taken while the
    /// request is in flight, nothing changes and the appointment stays where it was.
    /// </summary>
    /// <remarks>
    /// Bound by the cancellation window like cancelling, except for a stranded booking — one whose
    /// slot a schedule change has flagged — which the clinic caused and can always move.
    /// </remarks>
    Task<AppointmentChangeResult> RescheduleAsync(
        Guid appointmentId,
        Guid newSlotId,
        AppointmentCaller caller,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a booked appointment completed and announces <c>appointment.completed</c> with the
    /// doctor's clinical notes. Only the appointment's own doctor may, and only once it has
    /// started. The slot stays booked: the time was used.
    /// </summary>
    Task<AppointmentChangeResult> CompleteAsync(
        Guid appointmentId,
        string notes,
        AppointmentCaller caller,
        string correlationId,
        CancellationToken cancellationToken = default);
}
