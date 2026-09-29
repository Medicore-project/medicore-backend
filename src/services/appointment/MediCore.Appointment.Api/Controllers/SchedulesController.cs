using FluentValidation;
using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// Doctor working hours. A weekly schedule is several of these rows, one per working weekday.
/// </summary>
/// <remarks>
/// Every mutation here reconciles the doctor's slots, so responses carry an <c>impact</c> block
/// saying how many slots were created, removed, or flagged for a receptionist to chase.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/schedules")]
public sealed class SchedulesController : AppointmentControllerBase
{
    private readonly IValidator<CreateDoctorScheduleRequest> _createValidator;
    private readonly IValidator<UpdateDoctorScheduleRequest> _updateValidator;
    private readonly IDoctorScheduleService _service;

    public SchedulesController(
        IValidator<CreateDoctorScheduleRequest> createValidator,
        IValidator<UpdateDoctorScheduleRequest> updateValidator,
        IDoctorScheduleService service)
    {
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _service = service;
    }

    // ── GET /api/schedules/doctor/{doctorId} ──────────────────────────────────

    /// <summary>Every schedule for one doctor, paused ones included.</summary>
    [HttpGet("doctor/{doctorId:guid}")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(IReadOnlyList<DoctorScheduleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetForDoctor(Guid doctorId, CancellationToken cancellationToken) =>
        Ok(await _service.GetForDoctorAsync(doctorId, cancellationToken));

    // ── GET /api/schedules/{scheduleId} ───────────────────────────────────────

    /// <summary>A single schedule by its stable business identifier.</summary>
    [HttpGet("{scheduleId:guid}")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(DoctorScheduleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(Guid scheduleId, CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(scheduleId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    // ── POST /api/schedules ───────────────────────────────────────────────────

    /// <summary>
    /// Creates a schedule and generates its slots across the horizon.
    /// Returns 404 when the doctor is unknown or not bookable, and 409 when it would overlap an
    /// existing schedule for the same doctor and weekday.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleManager)]
    [ProducesResponseType(typeof(DoctorScheduleMutationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] CreateDoctorScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Schedule validation failed.");
        }

        var result = await _service.CreateAsync(request, CurrentActor(), cancellationToken);

        return result switch
        {
            ScheduleCreatedResult created => CreatedAtAction(
                nameof(GetById),
                new { scheduleId = created.Response.Schedule.ScheduleId },
                created.Response),
            ScheduleCreateOverlapResult overlap => ConflictProblem(
                $"This schedule overlaps an existing {overlap.DayOfWeek} schedule for the same doctor."),
            ScheduleCreateDoctorNotFoundResult => DoctorNotFoundProblem(),
            _ => throw new InvalidOperationException("Unknown schedule creation result.")
        };
    }

    // ── PUT /api/schedules/{scheduleId} ───────────────────────────────────────

    /// <summary>
    /// Updates a schedule's hours, slot length, effective dates or active flag, then reconciles.
    /// Future free slots outside the new window are removed and booked ones are flagged.
    /// </summary>
    [HttpPut("{scheduleId:guid}")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleManager)]
    [ProducesResponseType(typeof(DoctorScheduleMutationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        Guid scheduleId,
        [FromBody] UpdateDoctorScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Schedule validation failed.");
        }

        var result = await _service.UpdateAsync(scheduleId, request, CurrentActor(), cancellationToken);

        return result switch
        {
            ScheduleUpdatedResult updated => Ok(updated.Response),
            ScheduleUpdateNotFoundResult => NotFound(),
            ScheduleUpdateOverlapResult overlap => ConflictProblem(
                $"This schedule overlaps an existing {overlap.DayOfWeek} schedule for the same doctor."),
            _ => throw new InvalidOperationException("Unknown schedule update result.")
        };
    }

    // ── DELETE /api/schedules/{scheduleId} ────────────────────────────────────

    /// <summary>
    /// Soft-deletes a schedule and clears the slots it was producing. Bookings on those slots are
    /// flagged rather than lost.
    /// </summary>
    [HttpDelete("{scheduleId:guid}")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleManager)]
    [ProducesResponseType(typeof(SlotReconciliationSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid scheduleId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(scheduleId, CurrentActor(), cancellationToken);

        return result switch
        {
            // 200 with the impact rather than 204: the caller needs to see how many bookings were
            // flagged by the deletion.
            ScheduleDeletedResult deleted => Ok(deleted.Impact),
            ScheduleDeleteNotFoundResult => NotFound(),
            _ => throw new InvalidOperationException("Unknown schedule delete result.")
        };
    }

    // ── POST /api/schedules/{scheduleId}/regenerate ───────────────────────────

    /// <summary>
    /// Re-runs slot generation for the schedule's doctor without changing the schedule itself —
    /// useful once the horizon has rolled forward.
    /// </summary>
    [HttpPost("{scheduleId:guid}/regenerate")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleManager)]
    [ProducesResponseType(typeof(SlotReconciliationSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Regenerate(Guid scheduleId, CancellationToken cancellationToken)
    {
        var result = await _service.RegenerateAsync(scheduleId, cancellationToken);

        return result switch
        {
            ScheduleRegeneratedResult regenerated => Ok(regenerated.Impact),
            ScheduleRegenerateNotFoundResult => NotFound(),
            _ => throw new InvalidOperationException("Unknown schedule regenerate result.")
        };
    }
}
