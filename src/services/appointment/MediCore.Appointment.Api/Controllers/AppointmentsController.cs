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
    /// <summary>
    /// The widest date range one list request may cover — a quarter. Enough for any screen that
    /// exists; anything wider is a mistake or a scrape, not a view.
    /// </summary>
    public const int MaxListDays = 92;

    /// <summary>What an unbounded list request covers: today and the six days after it.</summary>
    public const int DefaultListDays = 7;

    private readonly IValidator<BookAppointmentRequest> _validator;
    private readonly IAppointmentBookingService _service;
    private readonly IAppointmentQueryService _queries;
    private readonly TimeProvider _timeProvider;

    public AppointmentsController(
        IValidator<BookAppointmentRequest> validator,
        IAppointmentBookingService service,
        IAppointmentQueryService queries,
        TimeProvider timeProvider)
    {
        _validator = validator;
        _service = service;
        _queries = queries;
        _timeProvider = timeProvider;
    }

    // ── GET /api/appointments ─────────────────────────────────────────────────

    /// <summary>
    /// What is booked — for one doctor or the whole clinic — on Asia/Colombo dates
    /// <c>from</c>..<c>to</c> inclusive. Defaults to the coming week. Every status is returned, so
    /// the caller decides whether a cancelled visit is worth showing.
    /// </summary>
    /// <remarks>
    /// This is what makes a booking visible to the clinic. The availability listing only ever
    /// returns free slots, so without it a booked slot simply vanished from the grid with no way to
    /// see who had taken it.
    /// </remarks>
    [HttpGet]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(IReadOnlyList<AppointmentSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? doctorId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var start = from ?? ColomboTime.Today(_timeProvider);
        var end = to ?? start.AddDays(DefaultListDays - 1);

        if (start > end)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "The 'from' date must not be after the 'to' date.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (end.DayNumber - start.DayNumber + 1 > MaxListDays)
        {
            return BadRequest(new ProblemDetails
            {
                Title = $"A list may cover at most {MaxListDays} days.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        return Ok(await _queries.ListAsync(
            doctorId == Guid.Empty ? null : doctorId,
            start,
            end,
            cancellationToken));
    }

    // ── GET /api/appointments/mine ────────────────────────────────────────────

    /// <summary>
    /// The booking token holder's own appointments that are still booked and yet to start.
    /// </summary>
    /// <remarks>
    /// Takes no patient id: the patient is the token's <c>patientId</c> claim and nothing else, so
    /// a token can only ever read the bookings of the one patient who identified to get it.
    /// </remarks>
    [HttpGet("mine")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.BookingHolder)]
    [ProducesResponseType(typeof(IReadOnlyList<PatientAppointmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken)
    {
        // BookingHolder guarantees the claim is present; a malformed value is still refused rather
        // than read as "nobody".
        if (CurrentBookingPatientId() is not { } patientId)
        {
            return ForbiddenProblem("This token does not name a patient.");
        }

        return Ok(await _queries.ListUpcomingForPatientAsync(patientId, cancellationToken));
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
    /// <para>
    /// Safe under concurrency (SCRUM-35): when several requests book the same slot at once, exactly
    /// one gets 201 and every other gets 409 with a message saying why, and a losing request leaves
    /// no appointment, slot change or event behind.
    /// </para>
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
            CurrentBookingPatientDetails(),
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
            BookingContendedResult => ConflictProblem(
                "Several people are trying to book this slot right now. Please choose it again or pick another."),
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

    // ── GET /api/appointments/{appointmentId}/history ─────────────────────────

    /// <summary>
    /// Every change to one appointment, oldest first: the booking, then each reschedule,
    /// cancellation or completion, with who made it and when.
    /// </summary>
    [HttpGet("{appointmentId:guid}/history")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(IReadOnlyList<AppointmentHistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHistory(Guid appointmentId, CancellationToken cancellationToken)
    {
        var history = await _queries.GetHistoryAsync(appointmentId, cancellationToken);
        return history is null ? NotFound() : Ok(history);
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
