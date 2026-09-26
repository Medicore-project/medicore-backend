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
}
