using FluentValidation;
using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// Generated appointment slots: what is bookable, what needs attention, and administrative blocks.
/// </summary>
/// <remarks>
/// Slots are never created here. They are produced by reconciliation whenever a schedule, holiday
/// or approved leave changes, which is what keeps the calendar and the schedules from drifting
/// apart.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/slots")]
public sealed class SlotsController : AppointmentControllerBase
{
    private readonly IValidator<BlockSlotRequest> _blockValidator;
    private readonly ISlotService _service;

    public SlotsController(IValidator<BlockSlotRequest> blockValidator, ISlotService service)
    {
        _blockValidator = blockValidator;
        _service = service;
    }

    // ── GET /api/slots/available ──────────────────────────────────────────────

    /// <summary>
    /// Bookable slots for a doctor. <c>from</c> and <c>to</c> are Asia/Colombo dates and default
    /// to the configured horizon.
    /// </summary>
    /// <remarks>
    /// Returns only free, future slots. This endpoint is the seam the public guest-booking flow
    /// (SCRUM-39) is expected to reuse, so it deliberately exposes nothing a patient could not
    /// legitimately book — a doctor who is unknown or no longer bookable gets 404, not their
    /// leftover slots.
    /// </remarks>
    [HttpGet("available")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(IReadOnlyList<SlotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAvailable(
        [FromQuery] Guid doctorId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (doctorId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "A doctorId is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (from is not null && to is not null && from > to)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "The 'from' date must not be after the 'to' date.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var result = await _service.GetAvailableAsync(doctorId, from, to, cancellationToken);

        return result switch
        {
            AvailableSlotsFoundResult found => Ok(found.Slots),
            AvailableSlotsDoctorNotFoundResult => DoctorNotFoundProblem(),
            _ => throw new InvalidOperationException("Unknown available slots result.")
        };
    }

    // ── GET /api/slots/flagged ────────────────────────────────────────────────

    /// <summary>
    /// Slots needing attention — bookings stranded by a schedule, holiday or leave change, soonest
    /// first. Omit <c>doctorId</c> for the whole clinic.
    /// </summary>
    [HttpGet("flagged")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(IReadOnlyList<SlotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetFlagged(
        [FromQuery] Guid? doctorId,
        CancellationToken cancellationToken) =>
        Ok(await _service.GetFlaggedAsync(
            doctorId == Guid.Empty ? null : doctorId,
            cancellationToken));

    // ── PATCH /api/slots/{slotId}/block ───────────────────────────────────────

    /// <summary>
    /// Suppresses a single free slot without touching the schedule behind it.
    /// Returns 409 when the slot is booked, already blocked, or flagged.
    /// </summary>
    [HttpPatch("{slotId:guid}/block")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleManager)]
    [ProducesResponseType(typeof(SlotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Block(
        Guid slotId,
        [FromBody] BlockSlotRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _blockValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Block request validation failed.");
        }

        var result = await _service.BlockAsync(slotId, request, CurrentActor(), cancellationToken);

        return result switch
        {
            SlotBlockedResult blocked => Ok(blocked.Slot),
            SlotBlockNotFoundResult => NotFound(),
            SlotBlockNotAvailableResult unavailable => ConflictProblem(
                $"Only an available slot can be blocked; this one is {unavailable.CurrentStatus}."),
            _ => throw new InvalidOperationException("Unknown slot block result.")
        };
    }

    // ── PATCH /api/slots/{slotId}/unblock ─────────────────────────────────────

    /// <summary>Returns a blocked slot to bookable.</summary>
    [HttpPatch("{slotId:guid}/unblock")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleManager)]
    [ProducesResponseType(typeof(SlotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Unblock(Guid slotId, CancellationToken cancellationToken)
    {
        var result = await _service.UnblockAsync(slotId, CurrentActor(), cancellationToken);

        return result switch
        {
            SlotUnblockedResult unblocked => Ok(unblocked.Slot),
            SlotUnblockNotFoundResult => NotFound(),
            SlotUnblockNotBlockedResult notBlocked => ConflictProblem(
                $"Only a blocked slot can be unblocked; this one is {notBlocked.CurrentStatus}."),
            _ => throw new InvalidOperationException("Unknown slot unblock result.")
        };
    }
}
