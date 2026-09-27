using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Services;

/// <summary>
/// Answers to a waitlist entry (SCRUM-37): accepting or declining an offer, and leaving the queue.
/// The patient answers with their booking token; the front desk may answer for them, typically on
/// the phone. <see cref="AppointmentCaller.PatientId"/> set means the caller may only touch their
/// own entries.
/// </summary>
public interface IWaitlistChangeService
{
    /// <summary>
    /// Takes the offered slot: creates the appointment exactly as booking would, and closes the
    /// entry as accepted — AC4. Refused at or after the offer's expiry, whether or not the sweeper
    /// has got to it yet.
    /// </summary>
    Task<WaitlistChangeResult> AcceptAsync(
        Guid waitlistEntryId,
        AppointmentCaller caller,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Turns the offer down; the slot passes to the next in line — AC3.</summary>
    Task<WaitlistChangeResult> DeclineAsync(
        Guid waitlistEntryId,
        AppointmentCaller caller,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the entry out of the queue: the patient leaving, or the front desk removing it. An
    /// entry holding an offer passes the slot on first.
    /// </summary>
    Task<WaitlistChangeResult> WithdrawAsync(
        Guid waitlistEntryId,
        AppointmentCaller caller,
        string? reason,
        CancellationToken cancellationToken = default);
}

/// <summary>The outcome of an answer to a waitlist entry. The controller picks the status code.</summary>
public abstract record WaitlistChangeResult;

/// <summary>The offer was taken and the appointment created.</summary>
public sealed record WaitlistAcceptedResult(AppointmentResponse Appointment, Guid WaitlistEntryId)
    : WaitlistChangeResult;

/// <summary>The entry was closed — declined or withdrawn.</summary>
public sealed record WaitlistEntryChangedResult(WaitlistEntryResponse Entry) : WaitlistChangeResult;

/// <summary>
/// No such entry — or, for a patient caller, not theirs. One answer on purpose, as with
/// appointments, so a booking token cannot probe which entry ids exist.
/// </summary>
public sealed record WaitlistEntryNotFoundResult : WaitlistChangeResult;

/// <summary>
/// The entry's status does not allow this answer: accepting or declining without an open offer,
/// or leaving a queue already left. <paramref name="Action"/> is "accept", "decline" or "withdraw".
/// </summary>
public sealed record WaitlistInvalidStateResult(string CurrentStatus, string Action) : WaitlistChangeResult;

/// <summary>The offer lapsed at <paramref name="ExpiredAtUtc"/>; the slot is passing to the next in line.</summary>
public sealed record WaitlistOfferExpiredResult(DateTime ExpiredAtUtc) : WaitlistChangeResult;

/// <summary>The doctor is no longer bookable, so the offer cannot be taken.</summary>
public sealed record WaitlistOfferDoctorNotFoundResult : WaitlistChangeResult;

/// <summary>
/// The offered slot is gone or no longer held for this entry. Should not happen while the offer
/// is open — schedule revision puts such an entry back to waiting — and is refused rather than
/// guessed at if it does.
/// </summary>
public sealed record WaitlistOfferedSlotUnavailableResult : WaitlistChangeResult;

/// <summary>
/// The patient has another booked appointment overlapping the offered time. The offer stays open,
/// so they can move the other appointment or decline this one.
/// </summary>
public sealed record WaitlistPatientOverlapResult(
    Guid ExistingAppointmentId,
    DateTime ExistingStartUtc,
    DateTime ExistingEndUtc) : WaitlistChangeResult;

/// <summary>Three attempts in a row lost a race; the caller is asked to try again.</summary>
public sealed record WaitlistChangeContendedResult : WaitlistChangeResult;

/// <summary>The <see cref="WaitlistInvalidStateResult.Action"/> values.</summary>
public static class WaitlistAction
{
    public const string Accept = "accept";
    public const string Decline = "decline";
    public const string Withdraw = "withdraw";
}
