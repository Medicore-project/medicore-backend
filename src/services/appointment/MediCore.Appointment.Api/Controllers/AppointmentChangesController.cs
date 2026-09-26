using FluentValidation;
using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Api.Middleware;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// Changes to an existing appointment: cancel (and, later in SCRUM-36, reschedule and complete).
/// </summary>
/// <remarks>
/// <para>
/// Shares the <c>api/appointments</c> prefix with <see cref="AppointmentsController"/>, which only
/// books and reads. Every change here commits the appointment, any slot change, a history entry and
/// any event row in one transaction.
/// </para>
/// <para>
/// Each change has two routes. <c>{id}/…</c> is for front-desk staff and returns the appointment.
/// <c>mine/{id}/…</c> is for a booking token, may only touch that token's own patient, and returns
/// 204. The public DTOs never carry the patient or slot id, and the page reloads its list anyway.
/// Two paths rather than one keeps the browser's booking-token routing unambiguous: a receptionist
/// holding a patient's token must still send their staff token to <c>{id}/cancel</c>.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/appointments")]
public sealed class AppointmentChangesController : AppointmentControllerBase
{
    private readonly IValidator<CancelAppointmentRequest> _cancelValidator;
    private readonly IAppointmentLifecycleService _service;

    public AppointmentChangesController(
        IValidator<CancelAppointmentRequest> cancelValidator,
        IAppointmentLifecycleService service)
    {
        _cancelValidator = cancelValidator;
        _service = service;
    }

    // ── PUT /api/appointments/{appointmentId}/cancel ──────────────────────────

    /// <summary>
    /// Cancels a booked appointment on a patient's behalf, releasing its slot and announcing
    /// <c>appointment.cancelled</c>.
    /// </summary>
    /// <remarks>
    /// Refused with 400 inside the cancellation window (<c>Appointments:Cancellation:WindowHours</c>,
    /// 24 by default), with a message stating the policy. Refused with 409 when the appointment is
    /// no longer booked.
    /// </remarks>
    [HttpPut("{appointmentId:guid}/cancel")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleManager)]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Cancel(
        Guid appointmentId,
        [FromBody] CancelAppointmentRequest request,
        CancellationToken cancellationToken) =>
        CancelAsync(appointmentId, request, new AppointmentCaller(CurrentActor(), StaffId: CurrentStaffId()), cancellationToken);

    // ── PUT /api/appointments/mine/{appointmentId}/cancel ─────────────────────

    /// <summary>
    /// Cancels one of the booking token holder's own appointments. Same rules as the staff route;
    /// an appointment belonging to anyone else is 404.
    /// </summary>
    [HttpPut("mine/{appointmentId:guid}/cancel")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.BookingHolder)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CancelMine(
        Guid appointmentId,
        [FromBody] CancelAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentBookingPatientId() is not { } patientId)
        {
            return ForbiddenProblem("This token does not name a patient.");
        }

        var result = await CancelAsync(
            appointmentId, request, new AppointmentCaller(CurrentActor(), patientId), cancellationToken);

        return result is OkObjectResult ? NoContent() : result;
    }

    private async Task<IActionResult> CancelAsync(
        Guid appointmentId,
        CancelAppointmentRequest request,
        AppointmentCaller caller,
        CancellationToken cancellationToken)
    {
        var validation = await _cancelValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Cancellation request validation failed.");
        }

        var result = await _service.CancelAsync(
            appointmentId, request.Reason, caller, CorrelationId(), cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>Maps every change result to its status code, for all the change endpoints.</summary>
    private IActionResult ToActionResult(AppointmentChangeResult result) => result switch
    {
        AppointmentChangedResult changed => Ok(changed.Appointment),
        AppointmentNotFoundResult => NotFound(new ProblemDetails
        {
            Title = "Appointment not found.",
            Status = StatusCodes.Status404NotFound
        }),
        AppointmentInvalidTransitionResult invalid => ConflictProblem(
            $"This appointment is {invalid.CurrentStatus} and can no longer be "
            + $"{invalid.Action.ToLowerInvariant()}."),
        AppointmentInsideCancellationWindowResult window => BadRequest(new ProblemDetails
        {
            Title = DescribeWindow(window),
            Status = StatusCodes.Status400BadRequest
        }),
        AppointmentContendedResult => ConflictProblem(
            "This appointment changed while we were updating it. Please reload and try again."),
        _ => throw new InvalidOperationException("Unknown appointment change result.")
    };

    /// <summary>
    /// The policy in words, and when this appointment starts, in Colombo time: SCRUM-36 asks the
    /// 400 to state the policy, and the patient knows the time in Colombo, not UTC.
    /// </summary>
    public static string DescribeWindow(AppointmentInsideCancellationWindowResult window)
    {
        var start = $"{ColomboTime.ToColombo(window.StartUtc):HH:mm} on "
            + $"{ColomboTime.ToColomboDate(window.StartUtc):dd MMM yyyy}";

        if (window.WindowHours == 0)
        {
            return $"This appointment started at {start}, so it can no longer be cancelled or rescheduled.";
        }

        var hours = window.WindowHours == 1 ? "1 hour" : $"{window.WindowHours} hours";

        return $"Appointments can only be cancelled or rescheduled up to {hours} before they start. "
            + $"This one starts at {start}.";
    }

    private string CorrelationId() =>
        HttpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString() ?? Guid.NewGuid().ToString();
}
