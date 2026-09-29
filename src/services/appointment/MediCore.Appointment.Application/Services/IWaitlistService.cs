using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Services;

/// <summary>
/// The waitlist for full clinic days (SCRUM-37). A queue is one doctor's day; patients join it
/// when every remaining time that day is taken, and are offered released times in order.
/// </summary>
public interface IWaitlistService
{
    /// <summary>
    /// Puts the patient at the back of the doctor's queue for the day — AC1. Refused unless the day
    /// is full, so the waitlist never stands between a patient and a time they could simply book.
    /// </summary>
    /// <param name="patientDetails">
    /// The patient's number and name from their booking token, kept on the entry so an offer
    /// accepted by the front desk still records who it is for. Null for a staff caller.
    /// </param>
    /// <param name="serviceCode">What the appointment will be billed as; null for a consultation.</param>
    Task<WaitlistJoinResult> JoinAsync(
        Guid doctorId,
        DateOnly date,
        Guid patientId,
        BookingPatientDetails? patientDetails,
        string? serviceCode,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The patient's active entries and those closed in the last <see cref="RecentlyClosedDays"/>
    /// days, so an offer that lapsed or was taken still shows what became of it.
    /// </summary>
    Task<IReadOnlyList<WaitlistEntryResponse>> GetMineAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Entries for days in the range, optionally for one doctor and in one status.
    /// <paramref name="status"/> is a <c>WaitlistStatus</c> value, or <see cref="ActiveFilter"/>
    /// for waiting and offered together; null for every status.
    /// </summary>
    Task<IReadOnlyList<WaitlistEntryResponse>> ListAsync(
        Guid? doctorId,
        DateOnly from,
        DateOnly to,
        string? status,
        CancellationToken cancellationToken = default);

    /// <summary>One entry, or null if there is none.</summary>
    Task<WaitlistEntryResponse?> GetAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The doctor's clinic days between two Colombo dates, each marked full or not, for the public
    /// booking page to offer the waitlist on a full day. Defaults to today through the slot
    /// horizon, and never looks outside it.
    /// </summary>
    Task<WaitlistDaysResult> GetDaysAsync(
        Guid doctorId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);

    /// <summary>How far back <see cref="GetMineAsync"/> shows closed entries.</summary>
    const int RecentlyClosedDays = 30;

    /// <summary>The <see cref="ListAsync"/> status filter meaning "waiting or offered".</summary>
    const string ActiveFilter = "Active";
}

/// <summary>The doctor's days, or that the doctor is not bookable.</summary>
public abstract record WaitlistDaysResult;

/// <summary>Every day in range with a clinic, soonest first.</summary>
public sealed record WaitlistDaysFoundResult(IReadOnlyList<PublicBookingDayResponse> Days) : WaitlistDaysResult;

/// <summary>The doctor is unknown or no longer bookable.</summary>
public sealed record WaitlistDaysDoctorNotFoundResult : WaitlistDaysResult;

/// <summary>The outcome of joining a waitlist. The controller picks the status code.</summary>
public abstract record WaitlistJoinResult;

/// <summary>The patient is in the queue.</summary>
public sealed record WaitlistJoinedResult(WaitlistEntryResponse Entry) : WaitlistJoinResult;

/// <summary>The doctor is unknown or no longer bookable.</summary>
public sealed record WaitlistDoctorNotFoundResult : WaitlistJoinResult;

/// <summary>The day has already gone.</summary>
public sealed record WaitlistDateInPastResult : WaitlistJoinResult;

/// <summary>
/// The day is past the slot horizon, so no slots exist yet and the day cannot be full. Once they
/// are generated the patient can simply book.
/// </summary>
public sealed record WaitlistBeyondHorizonResult(int HorizonDays) : WaitlistJoinResult;

/// <summary>
/// The doctor has no times left to wait for that day: not working, on leave, a holiday, every
/// slot blocked, or the clinic already over.
/// </summary>
public sealed record WaitlistNoClinicThatDayResult : WaitlistJoinResult;

/// <summary>The day still has times anyone can book, so there is nothing to wait for.</summary>
public sealed record WaitlistDayNotFullResult(int FreeSlots) : WaitlistJoinResult;

/// <summary>The patient is already waiting in, or holding an offer from, this queue.</summary>
public sealed record WaitlistAlreadyWaitingResult : WaitlistJoinResult;

/// <summary>The patient already has an appointment with this doctor that day.</summary>
public sealed record WaitlistAlreadyBookedResult : WaitlistJoinResult;

/// <summary>The patient is already waiting in as many queues as the clinic allows.</summary>
public sealed record WaitlistTooManyEntriesResult(int Limit) : WaitlistJoinResult;

/// <summary>Three attempts in a row lost a race; the caller is asked to try again.</summary>
public sealed record WaitlistJoinContendedResult : WaitlistJoinResult;
