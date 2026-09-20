using FluentValidation;
using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// Doctor leave and its approval workflow.
/// </summary>
/// <remarks>
/// Requesting leave and being granted it are separate acts, and separate permissions. Submitting
/// falls under <see cref="AppointmentAuthorizationPolicies.LeaveManager"/> and records a pending
/// request that changes nothing; only <see cref="AppointmentAuthorizationPolicies.LeaveApprover"/>
/// can decide it, and only then do the doctor's slots change. Doctors are deliberately outside the
/// approver policy, so nobody approves their own request.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/doctor-leaves")]
public sealed class DoctorLeavesController : AppointmentControllerBase
{
    private readonly IValidator<CreateDoctorLeaveRequest> _createValidator;
    private readonly IValidator<ReviewDoctorLeaveRequest> _reviewValidator;
    private readonly IDoctorLeaveService _service;

    public DoctorLeavesController(
        IValidator<CreateDoctorLeaveRequest> createValidator,
        IValidator<ReviewDoctorLeaveRequest> reviewValidator,
        IDoctorLeaveService service)
    {
        _createValidator = createValidator;
        _reviewValidator = reviewValidator;
        _service = service;
    }

    // ── GET /api/doctor-leaves/doctor/{doctorId} ──────────────────────────────

    /// <summary>Every leave request for one doctor, whatever its status.</summary>
    [HttpGet("doctor/{doctorId:guid}")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(IReadOnlyList<DoctorLeaveResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetForDoctor(Guid doctorId, CancellationToken cancellationToken) =>
        Ok(await _service.GetForDoctorAsync(doctorId, cancellationToken));

    // ── GET /api/doctor-leaves/pending ────────────────────────────────────────

    /// <summary>
    /// The approval queue: requests still awaiting a decision, soonest start date first.
    /// </summary>
    [HttpGet("pending")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.LeaveApprover)]
    [ProducesResponseType(typeof(IReadOnlyList<DoctorLeaveResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPending(CancellationToken cancellationToken) =>
        Ok(await _service.GetPendingAsync(cancellationToken));

    // ── POST /api/doctor-leaves ───────────────────────────────────────────────

    /// <summary>
    /// Submits a leave request. It is recorded as pending and has no effect on the doctor's slots
    /// until an administrator approves it.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AppointmentAuthorizationPolicies.LeaveManager)]
    [ProducesResponseType(typeof(DoctorLeaveResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] CreateDoctorLeaveRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Leave request validation failed.");
        }

        var result = await _service.CreateAsync(request, CurrentActor(), cancellationToken);

        return result switch
        {
            LeaveCreatedResult created => CreatedAtAction(
                nameof(GetForDoctor),
                new { doctorId = created.Leave.DoctorId },
                created.Leave),
            _ => throw new InvalidOperationException("Unknown leave creation result.")
        };
    }

    // ── PATCH /api/doctor-leaves/{leaveId}/review ─────────────────────────────

    /// <summary>
    /// Approves or rejects a request. Approving removes the doctor's free slots for those dates
    /// and flags any bookings on them; the response says how many of each.
    /// </summary>
    /// <remarks>
    /// Returns 409 when the decision is not a legal move — re-approving an approved request, or
    /// trying to return one to pending.
    /// </remarks>
    [HttpPatch("{leaveId:guid}/review")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.LeaveApprover)]
    [ProducesResponseType(typeof(DoctorLeaveReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Review(
        Guid leaveId,
        [FromBody] ReviewDoctorLeaveRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _reviewValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Leave review validation failed.");
        }

        var result = await _service.ReviewAsync(leaveId, request, CurrentActor(), cancellationToken);

        return result switch
        {
            LeaveReviewedResult reviewed => Ok(reviewed.Response),
            LeaveReviewNotFoundResult => NotFound(),
            LeaveReviewInvalidTransitionResult invalid => ConflictProblem(
                $"A leave request that is {invalid.From} cannot be changed to {invalid.To}."),
            _ => throw new InvalidOperationException("Unknown leave review result.")
        };
    }

    // ── DELETE /api/doctor-leaves/{leaveId} ───────────────────────────────────

    /// <summary>
    /// Withdraws a request. If it had been approved, the doctor's slots for those dates are
    /// regenerated.
    /// </summary>
    [HttpDelete("{leaveId:guid}")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.LeaveManager)]
    [ProducesResponseType(typeof(SlotReconciliationSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Withdraw(Guid leaveId, CancellationToken cancellationToken)
    {
        var result = await _service.WithdrawAsync(leaveId, CurrentActor(), cancellationToken);

        return result switch
        {
            LeaveWithdrawnResult withdrawn => Ok(withdrawn.Impact),
            LeaveWithdrawNotFoundResult => NotFound(),
            _ => throw new InvalidOperationException("Unknown leave withdraw result.")
        };
    }
}
