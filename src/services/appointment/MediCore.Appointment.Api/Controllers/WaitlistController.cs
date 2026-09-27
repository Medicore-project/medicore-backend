using FluentValidation;
using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Api.Middleware;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// The waitlist for full clinic days (SCRUM-37): join a doctor's full day, see where you stand,
/// and answer an offer when a slot is released.
/// </summary>
/// <remarks>
/// <para>
/// The same split as appointment changes. <c>mine/…</c> routes are for a booking token, touch only
/// that token's patient, and answer 204 or the patient's reduced view. The other routes are for the
/// clinic: any clinic role may read the queues, and the front desk may answer an offer or remove
/// an entry on the patient's behalf — typically on the phone, since patients are not notified.
/// Separate paths let the browser choose the right token by path alone.
/// </para>
/// <para>
/// Joining is one route for both, as booking is: a booking token's patient always wins over the
/// body.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/waitlist")]
public sealed class WaitlistController : AppointmentControllerBase
{
    /// <summary>A list covers the next two weeks unless asked otherwise.</summary>
    public const int DefaultListDays = 14;

    /// <summary>The widest list, as for booked appointments.</summary>
    public const int MaxListDays = 92;

    private readonly IValidator<JoinWaitlistRequest> _joinValidator;
    private readonly IValidator<RemoveWaitlistEntryRequest> _removeValidator;
    private readonly IWaitlistService _waitlist;
    private readonly IWaitlistChangeService _changes;
    private readonly TimeProvider _timeProvider;

    public WaitlistController(
        IValidator<JoinWaitlistRequest> joinValidator,
        IValidator<RemoveWaitlistEntryRequest> removeValidator,
        IWaitlistService waitlist,
        IWaitlistChangeService changes,
        TimeProvider timeProvider)
    {
        _joinValidator = joinValidator;
        _removeValidator = removeValidator;
        _waitlist = waitlist;
        _changes = changes;
        _timeProvider = timeProvider;
    }

    // ── POST /api/waitlist ────────────────────────────────────────────────────

    /// <summary>
    /// Joins the doctor's waitlist for a full day, at the back of the queue (AC1).
    /// </summary>
    /// <remarks>
    /// Open to front-desk staff and to a booking token. With a booking token the patient comes from
    /// the token and any <c>patientId</c> in the body is ignored. Refused with 409 while the day
    /// still has a free time — book it instead — and with 400 for a past day, a day beyond the slot
    /// horizon, or a day with no clinic.
    /// </remarks>
    [HttpPost]
    [Authorize(Policy = AppointmentAuthorizationPolicies.BookingCreator)]
    [ProducesResponseType(typeof(WaitlistEntryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Join([FromBody] JoinWaitlistRequest request, CancellationToken cancellationToken)
    {
        var validation = await _joinValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Waitlist request validation failed.");
        }

        var tokenPatientId = CurrentBookingPatientId();
        var patientId = tokenPatientId ?? request.PatientId ?? Guid.Empty;

        if (patientId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "A patientId is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var result = await _waitlist.JoinAsync(
            request.DoctorId,
            request.Date,
            patientId,
            CurrentBookingPatientDetails(),
            request.ServiceCode,
            CurrentActor(),
            cancellationToken);

        return result switch
        {
            WaitlistJoinedResult joined => tokenPatientId is null
                ? CreatedAtAction(nameof(GetById), new { waitlistEntryId = joined.Entry.WaitlistEntryId }, joined.Entry)
                : StatusCode(StatusCodes.Status201Created, WaitlistMapping.ToPatientResponse(joined.Entry)),
            WaitlistDoctorNotFoundResult => DoctorNotFoundProblem(),
            WaitlistDateInPastResult => BadRequest(new ProblemDetails
            {
                Title = "That day has already passed.",
                Status = StatusCodes.Status400BadRequest
            }),
            WaitlistBeyondHorizonResult beyond => BadRequest(new ProblemDetails
            {
                Title = $"Appointments open {beyond.HorizonDays} days ahead, so that day has no times yet. "
                    + "Please book once it opens.",
                Status = StatusCodes.Status400BadRequest
            }),
            WaitlistNoClinicThatDayResult => BadRequest(new ProblemDetails
            {
                Title = "The doctor has no clinic times left that day, so there is nothing to wait for.",
                Status = StatusCodes.Status400BadRequest
            }),
            WaitlistDayNotFullResult notFull => ConflictProblem(
                notFull.FreeSlots == 1
                    ? "That day still has a free time. Please book it instead."
                    : $"That day still has {notFull.FreeSlots} free times. Please book one instead."),
            WaitlistAlreadyWaitingResult => ConflictProblem("This patient is already on the waitlist for that day."),
            WaitlistAlreadyBookedResult => ConflictProblem(
                "This patient already has an appointment with this doctor that day."),
            WaitlistTooManyEntriesResult tooMany => ConflictProblem(
                $"A patient can be on the waitlist for at most {tooMany.Limit} days at a time."),
            WaitlistJoinContendedResult => ConflictProblem(
                "The waitlist changed while we were updating it. Please try again."),
            _ => throw new InvalidOperationException("Unknown waitlist join result.")
        };
    }

    // ── GET /api/waitlist/mine ────────────────────────────────────────────────

    /// <summary>
    /// The booking token holder's waitlist entries: every active one, with their place in line or
    /// open offer, and those closed in the last 30 days.
    /// </summary>
    [HttpGet("mine")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.BookingHolder)]
    [ProducesResponseType(typeof(IReadOnlyList<PatientWaitlistEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken)
    {
        if (CurrentBookingPatientId() is not { } patientId)
        {
            return ForbiddenProblem("This token does not name a patient.");
        }

        var entries = await _waitlist.GetMineAsync(patientId, cancellationToken);

        return Ok(entries.Select(WaitlistMapping.ToPatientResponse).ToList());
    }

    // ── PUT /api/waitlist/mine/{id}/accept ────────────────────────────────────

    /// <summary>
    /// Accepts the token holder's open offer: the appointment is booked and the entry cleared
    /// (AC4). 409 once the offer has expired, even if it still shows.
    /// </summary>
    [HttpPut("mine/{waitlistEntryId:guid}/accept")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.BookingHolder)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> AcceptMine(Guid waitlistEntryId, CancellationToken cancellationToken) =>
        AsPatientAsync(caller => _changes.AcceptAsync(waitlistEntryId, caller, CorrelationId(), cancellationToken));

    // ── PUT /api/waitlist/mine/{id}/decline ───────────────────────────────────

    /// <summary>Declines the token holder's open offer; it passes to the next in line (AC3).</summary>
    [HttpPut("mine/{waitlistEntryId:guid}/decline")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.BookingHolder)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> DeclineMine(Guid waitlistEntryId, CancellationToken cancellationToken) =>
        AsPatientAsync(caller => _changes.DeclineAsync(waitlistEntryId, caller, cancellationToken));

    // ── DELETE /api/waitlist/mine/{id} ────────────────────────────────────────

    /// <summary>
    /// Leaves the waitlist. An open offer is passed to the next in line first.
    /// </summary>
    [HttpDelete("mine/{waitlistEntryId:guid}")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.BookingHolder)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> LeaveMine(Guid waitlistEntryId, CancellationToken cancellationToken) =>
        AsPatientAsync(caller => _changes.WithdrawAsync(waitlistEntryId, caller, reason: null, cancellationToken));

    // ── GET /api/waitlist ─────────────────────────────────────────────────────

    /// <summary>
    /// Waitlist entries for Colombo dates <c>from</c>..<c>to</c> inclusive (default: the next two
    /// weeks), optionally for one doctor. <c>status</c> is one entry status, or <c>Active</c> for
    /// waiting and offered together; omitted, every status is returned.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(IReadOnlyList<WaitlistEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? doctorId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? status,
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

        if (status is not null && !ListableStatuses.Contains(status))
        {
            return BadRequest(new ProblemDetails
            {
                Title = $"Status must be one of: {string.Join(", ", ListableStatuses)}.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        return Ok(await _waitlist.ListAsync(
            doctorId == Guid.Empty ? null : doctorId, start, end, status, cancellationToken));
    }

    /// <summary>What the <c>status</c> filter accepts, exactly as spelled.</summary>
    public static readonly IReadOnlyList<string> ListableStatuses =
    [
        IWaitlistService.ActiveFilter,
        WaitlistStatus.Waiting,
        WaitlistStatus.Offered,
        WaitlistStatus.Accepted,
        WaitlistStatus.Declined,
        WaitlistStatus.Expired,
        WaitlistStatus.Withdrawn
    ];

    // ── GET /api/waitlist/{id} ────────────────────────────────────────────────

    /// <summary>One waitlist entry.</summary>
    [HttpGet("{waitlistEntryId:guid}")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(WaitlistEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(Guid waitlistEntryId, CancellationToken cancellationToken) =>
        await _waitlist.GetAsync(waitlistEntryId, cancellationToken) is { } entry
            ? Ok(entry)
            : EntryNotFound();

    // ── PUT /api/waitlist/{id}/accept ─────────────────────────────────────────

    /// <summary>
    /// Accepts an open offer on the patient's behalf — a patient phoning the front desk. Books the
    /// appointment and clears the entry (AC4).
    /// </summary>
    [HttpPut("{waitlistEntryId:guid}/accept")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleManager)]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Accept(Guid waitlistEntryId, CancellationToken cancellationToken) =>
        ToActionResult(await _changes.AcceptAsync(waitlistEntryId, StaffCaller(), CorrelationId(), cancellationToken));

    // ── PUT /api/waitlist/{id}/decline ────────────────────────────────────────

    /// <summary>Declines an open offer on the patient's behalf; it passes to the next in line (AC3).</summary>
    [HttpPut("{waitlistEntryId:guid}/decline")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleManager)]
    [ProducesResponseType(typeof(WaitlistEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Decline(Guid waitlistEntryId, CancellationToken cancellationToken) =>
        ToActionResult(await _changes.DeclineAsync(waitlistEntryId, StaffCaller(), cancellationToken));

    // ── DELETE /api/waitlist/{id} ─────────────────────────────────────────────

    /// <summary>
    /// Removes an entry from the waitlist, with an optional reason kept on it. An open offer is
    /// passed to the next in line first. The body may be omitted.
    /// </summary>
    [HttpDelete("{waitlistEntryId:guid}")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleManager)]
    [ProducesResponseType(typeof(WaitlistEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Remove(
        Guid waitlistEntryId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RemoveWaitlistEntryRequest? request,
        CancellationToken cancellationToken)
    {
        request ??= new RemoveWaitlistEntryRequest(null);

        var validation = await _removeValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Waitlist removal validation failed.");
        }

        return ToActionResult(
            await _changes.WithdrawAsync(waitlistEntryId, StaffCaller(), request.Reason, cancellationToken));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a change as the booking token's patient. Success is 204: the patient's page reloads its
    /// own lists, and the staff shapes carry ids a patient has no use for.
    /// </summary>
    private async Task<IActionResult> AsPatientAsync(Func<AppointmentCaller, Task<WaitlistChangeResult>> change)
    {
        if (CurrentBookingPatientId() is not { } patientId)
        {
            return ForbiddenProblem("This token does not name a patient.");
        }

        var result = await change(new AppointmentCaller(CurrentActor(), patientId));

        return result is WaitlistAcceptedResult or WaitlistEntryChangedResult
            ? NoContent()
            : ToActionResult(result);
    }

    private AppointmentCaller StaffCaller() => new(CurrentActor(), StaffId: CurrentStaffId());

    /// <summary>Maps every change result to its status code, for all the answer endpoints.</summary>
    private IActionResult ToActionResult(WaitlistChangeResult result) => result switch
    {
        WaitlistAcceptedResult accepted => CreatedAtAction(
            nameof(AppointmentsController.GetById),
            "Appointments",
            new { appointmentId = accepted.Appointment.AppointmentId },
            accepted.Appointment),
        WaitlistEntryChangedResult changed => Ok(changed.Entry),
        WaitlistEntryNotFoundResult => EntryNotFound(),
        WaitlistInvalidStateResult invalid => ConflictProblem(DescribeInvalidState(invalid)),
        WaitlistOfferExpiredResult expired => ConflictProblem(
            $"This offer expired at {ColomboTime.ToColombo(expired.ExpiredAtUtc):HH:mm} on "
            + $"{ColomboTime.ToColomboDate(expired.ExpiredAtUtc):dd MMM yyyy} and has passed to the next patient."),
        WaitlistOfferDoctorNotFoundResult => DoctorNotFoundProblem(),
        WaitlistOfferedSlotUnavailableResult => ConflictProblem("The offered time is no longer available."),
        WaitlistPatientOverlapResult overlap => ConflictProblem(
            DescribeClash(overlap.ExistingStartUtc, overlap.ExistingEndUtc)),
        WaitlistChangeContendedResult => ConflictProblem(
            "The waitlist changed while we were updating it. Please reload and try again."),
        _ => throw new InvalidOperationException("Unknown waitlist change result.")
    };

    /// <summary>The 409 for an answer the entry's status does not allow, in words.</summary>
    public static string DescribeInvalidState(WaitlistInvalidStateResult invalid) => invalid.Action switch
    {
        WaitlistAction.Withdraw => $"This entry is already {invalid.CurrentStatus} and is no longer on the waitlist.",
        _ => $"There is no open offer to {invalid.Action}: this entry is {invalid.CurrentStatus}."
    };

    private IActionResult EntryNotFound() =>
        NotFound(new ProblemDetails
        {
            Title = "Waitlist entry not found.",
            Status = StatusCodes.Status404NotFound
        });

    private string CorrelationId() =>
        HttpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString() ?? Guid.NewGuid().ToString();
}
