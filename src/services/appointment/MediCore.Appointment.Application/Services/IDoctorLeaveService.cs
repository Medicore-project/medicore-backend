using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Services;

// ── Discriminated union results ───────────────────────────────────────────────

public abstract record LeaveCreateResult;

/// <summary>
/// The request was recorded as pending. No slots changed — that only happens on approval.
/// </summary>
public sealed record LeaveCreatedResult(DoctorLeaveResponse Leave) : LeaveCreateResult;

/// <summary>
/// The doctor is not in the doctor cache, or is there but no longer bookable (SCRUM-33). The
/// controller maps this to 404.
/// </summary>
public sealed record LeaveCreateDoctorNotFoundResult : LeaveCreateResult;

public abstract record LeaveReviewResult;
public sealed record LeaveReviewedResult(DoctorLeaveReviewResponse Response) : LeaveReviewResult;
public sealed record LeaveReviewNotFoundResult : LeaveReviewResult;

/// <summary>
/// The decision is not a legal move from the request's current status — for example approving a
/// request that is already approved. The controller maps this to 409.
/// </summary>
public sealed record LeaveReviewInvalidTransitionResult(string From, string To) : LeaveReviewResult;

public abstract record LeaveWithdrawResult;
public sealed record LeaveWithdrawnResult(SlotReconciliationSummary Impact) : LeaveWithdrawResult;
public sealed record LeaveWithdrawNotFoundResult : LeaveWithdrawResult;

/// <summary>
/// The caller is not the doctor this request belongs to. The controller maps this to 403.
/// </summary>
public sealed record LeaveWithdrawForbiddenResult : LeaveWithdrawResult;

// ── Service contract ──────────────────────────────────────────────────────────

/// <summary>
/// Doctor leave, including the approval workflow.
/// </summary>
/// <remarks>
/// Requesting leave and being granted it are separate acts. Creating a request changes nothing
/// about the doctor's calendar; only an administrator's approval does, and only then is
/// reconciliation run.
/// </remarks>
public interface IDoctorLeaveService
{
    /// <summary>Every leave request for one doctor, whatever its status.</summary>
    Task<IReadOnlyList<DoctorLeaveResponse>> GetForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approved leave for one doctor overlapping <paramref name="from"/>..<paramref name="to"/> —
    /// the dates on that doctor's calendar with no slots because leave suppressed them, as opposed
    /// to no schedule or a public holiday. Meant for a booking UI to explain an empty day.
    /// </summary>
    Task<IReadOnlyList<DoctorLeaveResponse>> GetApprovedBetweenAsync(
        Guid doctorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>The approval queue — every request still awaiting a decision.</summary>
    Task<IReadOnlyList<DoctorLeaveResponse>> GetPendingAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Submits a request. Always lands as pending, whoever submits it.</summary>
    Task<LeaveCreateResult> CreateAsync(
        CreateDoctorLeaveRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an administrator's approval or rejection, then reconciles the doctor's slots if the
    /// decision changed whether the leave suppresses them.
    /// </summary>
    Task<LeaveReviewResult> ReviewAsync(
        Guid leaveId,
        ReviewDoctorLeaveRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws a request. Reconciliation runs only when the request had been approved, since a
    /// pending or rejected one was never affecting the calendar.
    /// </summary>
    /// <param name="actorStaffId">
    /// The caller's own Staff/Doctor id. A doctor may withdraw only their own request — a mismatch,
    /// or a caller with no staff id at all, returns <see cref="LeaveWithdrawForbiddenResult"/>.
    /// </param>
    Task<LeaveWithdrawResult> WithdrawAsync(
        Guid leaveId,
        string actor,
        Guid? actorStaffId,
        CancellationToken cancellationToken = default);
}
