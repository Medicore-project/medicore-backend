using FluentValidation;
using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Api.Middleware;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// Confirmed bookings.
/// </summary>
/// <remarks>
/// Booking takes a slot that slot generation already produced; nothing here creates slots. A
/// successful booking marks the slot taken and writes an <c>appointment.booked</c> outbox row in
/// the same transaction, so the event survives a broker outage.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/appointments")]
public sealed class AppointmentsController : AppointmentControllerBase
{
    private readonly IValidator<BookAppointmentRequest> _validator;
    private readonly IAppointmentBookingService _service;

    public AppointmentsController(
        IValidator<BookAppointmentRequest> validator,
        IAppointmentBookingService service)
    {
        _validator = validator;
        _service = service;
    }

    // ── POST /api/appointments ────────────────────────────────────────────────

    /// <summary>
    /// Books an available slot.
    /// </summary>
    /// <remarks>
    /// Open to front-desk staff and to anyone holding a booking token from the Patient service's
    /// identify or public-register endpoint. When a booking token is used the patient comes from
    /// its <c>patientId</c> claim and <strong>any patientId in the body is ignored</strong>, so the
    /// token can only ever book for the one patient it names.
    /// </remarks>
    [HttpPost]
    [Authorize(Policy = AppointmentAuthorizationPolicies.BookingCreator)]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Book(
        [FromBody] BookAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Booking request validation failed.");
        }

        // A booking token names exactly one patient, so the body cannot redirect the booking
        // elsewhere: the claim wins outright. Ignoring the body rather than comparing the two and
        // refusing a mismatch is deliberate — a comparison is a branch that can be written wrong,
        // and the public booking page has no reason to send one at all. Only a staff caller, who
        // reached this action through the role half of BookingCreator, ever supplies a patientId.
        var patientId = CurrentBookingPatientId() ?? request.PatientId;

        if (patientId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "A patientId is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
            ?? Guid.NewGuid().ToString();

        var result = await _service.BookAsync(
            request.SlotId,
            patientId,
            request.ServiceCode,
            CurrentActor(),
            correlationId,
            cancellationToken);

        return result switch
        {
            BookingCreatedResult created => CreatedAtAction(
                nameof(GetById),
                new { appointmentId = created.Appointment.AppointmentId },
                created.Appointment),
            BookingSlotNotFoundResult => NotFound(new ProblemDetails
            {
                Title = "That slot no longer exists.",
                Status = StatusCodes.Status404NotFound
            }),
            BookingDoctorNotFoundResult => DoctorNotFoundProblem(),
            BookingSlotNotAvailableResult unavailable => ConflictProblem(
                $"This slot is no longer available; it is {unavailable.CurrentStatus}."),
            BookingSlotInPastResult => BadRequest(new ProblemDetails
            {
                Title = "That appointment time has already passed. Please choose a later slot.",
                Status = StatusCodes.Status400BadRequest
            }),
            BookingPatientOverlapResult overlap => ConflictProblem(DescribeClash(overlap)),
            BookingSlotTakenResult => ConflictProblem(
                "Someone booked this slot a moment ago. Please choose another."),
            _ => throw new InvalidOperationException("Unknown booking result.")
        };
    }

    // ── GET /api/appointments/{appointmentId} ─────────────────────────────────

    /// <summary>One booking by its id.</summary>
    [HttpGet("{appointmentId:guid}")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(Guid appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await _service.GetByIdAsync(appointmentId, cancellationToken);
        return appointment is null ? NotFound() : Ok(appointment);
    }

    /// <summary>
    /// SCRUM-34 AC3 asks the clash to be explained, so the existing appointment's window is spelled
    /// out in Colombo time — the form the patient recognises, since UTC would be five and a half
    /// hours off what they were told.
    /// </summary>
    private static string DescribeClash(BookingPatientOverlapResult overlap) =>
        $"This patient already has an appointment on "
        + $"{ColomboTime.ToColomboDate(overlap.ExistingStartUtc):dd MMM yyyy} from "
        + $"{ColomboTime.ToColombo(overlap.ExistingStartUtc):HH:mm} to "
        + $"{ColomboTime.ToColombo(overlap.ExistingEndUtc):HH:mm}.";
}
