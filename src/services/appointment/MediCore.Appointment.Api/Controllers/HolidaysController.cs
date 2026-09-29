using FluentValidation;
using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// Clinic-wide closures. Declaring one suppresses that date for every doctor (SCRUM-32 AC3).
/// </summary>
/// <remarks>
/// Writes are restricted to <see cref="AppointmentAuthorizationPolicies.HolidayManager"/> — a
/// narrower policy than schedule management, because one holiday reshapes the whole clinic's
/// calendar.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/holidays")]
public sealed class HolidaysController : AppointmentControllerBase
{
    private readonly IValidator<CreatePublicHolidayRequest> _createValidator;
    private readonly IPublicHolidayService _service;

    public HolidaysController(
        IValidator<CreatePublicHolidayRequest> createValidator,
        IPublicHolidayService service)
    {
        _createValidator = createValidator;
        _service = service;
    }

    // ── GET /api/holidays ─────────────────────────────────────────────────────

    /// <summary>Every declared closure, earliest first.</summary>
    [HttpGet]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(IReadOnlyList<PublicHolidayResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await _service.GetAllAsync(cancellationToken));

    // ── POST /api/holidays ────────────────────────────────────────────────────

    /// <summary>
    /// Declares a closure, clearing that date from every doctor's calendar and flagging any
    /// bookings already on it. Returns 409 when the date is already declared.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AppointmentAuthorizationPolicies.HolidayManager)]
    [ProducesResponseType(typeof(PublicHolidayMutationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePublicHolidayRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Holiday validation failed.");
        }

        var result = await _service.CreateAsync(request, CurrentActor(), cancellationToken);

        return result switch
        {
            HolidayCreatedResult created => CreatedAtAction(
                nameof(GetAll),
                null,
                created.Response),
            HolidayCreateDuplicateResult duplicate => ConflictProblem(
                $"A public holiday is already declared on {duplicate.Date:yyyy-MM-dd}."),
            _ => throw new InvalidOperationException("Unknown holiday creation result.")
        };
    }

    // ── DELETE /api/holidays/{holidayId} ──────────────────────────────────────

    /// <summary>
    /// Withdraws a closure. Slots are regenerated for that date wherever a schedule covers it.
    /// </summary>
    [HttpDelete("{holidayId:guid}")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.HolidayManager)]
    [ProducesResponseType(typeof(SlotReconciliationSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid holidayId, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(holidayId, CurrentActor(), cancellationToken);

        return result switch
        {
            HolidayDeletedResult deleted => Ok(deleted.Impact),
            HolidayDeleteNotFoundResult => NotFound(),
            _ => throw new InvalidOperationException("Unknown holiday delete result.")
        };
    }
}
