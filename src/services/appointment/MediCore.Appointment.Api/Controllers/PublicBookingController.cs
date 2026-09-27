using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// The reads the public booking page needs before anyone has identified themselves: which
/// specializations the clinic offers, which doctors practise them, when those doctors are free,
/// and which of their days are full.
/// </summary>
/// <remarks>
/// <para>
/// A separate controller rather than <c>[AllowAnonymous]</c> on <see cref="DoctorsController"/> and
/// <see cref="SlotsController"/>. Those are guarded by a test asserting no method on them is
/// anonymous, precisely because the gateway does not authenticate and a missing
/// <c>[Authorize]</c> would publish the staff-facing shapes. Keeping the anonymous surface in one
/// file also means it can be reviewed whole, and it returns reduced DTOs by construction rather
/// than by an <c>if (User.Identity.IsAuthenticated)</c> branch that would leak the day someone
/// edits it.
/// </para>
/// <para>
/// Booking itself is <strong>not</strong> here: <c>POST /api/appointments</c> requires a booking
/// token naming the patient. These endpoints are read-only.
/// </para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public/booking")]
[EnableRateLimiting(PublicRateLimitPolicies.PublicBooking)]
public sealed class PublicBookingController : AppointmentControllerBase
{
    private readonly IDoctorDirectoryService _doctorDirectory;
    private readonly ISlotService _slotService;
    private readonly IWaitlistService _waitlist;

    public PublicBookingController(
        IDoctorDirectoryService doctorDirectory,
        ISlotService slotService,
        IWaitlistService waitlist)
    {
        _doctorDirectory = doctorDirectory;
        _slotService = slotService;
        _waitlist = waitlist;
    }

    // ── GET /api/public/booking/specializations ───────────────────────────────

    /// <summary>
    /// The specializations that currently have a bookable doctor, ordered by name.
    /// </summary>
    [HttpGet("specializations")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSpecializations(CancellationToken cancellationToken) =>
        Ok(await _doctorDirectory.ListSpecializationsAsync(cancellationToken));

    // ── GET /api/public/booking/doctors ───────────────────────────────────────

    /// <summary>
    /// Bookable doctors, ordered by name. <c>specialization</c> narrows to one by whole name,
    /// ignoring case.
    /// </summary>
    /// <remarks>
    /// Deactivated doctors and staff who have lost the Doctor role are absent. An empty list on a
    /// fresh deployment usually means the doctor-cache backfill has not been run yet.
    /// </remarks>
    [HttpGet("doctors")]
    [ProducesResponseType(typeof(IReadOnlyList<PublicDoctorResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDoctors(
        [FromQuery] string? specialization,
        CancellationToken cancellationToken)
    {
        var doctors = await _doctorDirectory.ListBookableAsync(specialization, cancellationToken);

        return Ok(doctors
            .Select(doctor => new PublicDoctorResponse(
                doctor.DoctorId,
                doctor.FullName,
                doctor.Specialization))
            .ToList());
    }

    // ── GET /api/public/booking/slots ─────────────────────────────────────────

    /// <summary>
    /// Free times for one doctor. <c>from</c> and <c>to</c> are Asia/Colombo dates and default to
    /// the configured horizon.
    /// </summary>
    /// <remarks>
    /// Goes through the same service the staff availability listing uses, so it inherits
    /// available-only, future-only, and a 404 for a doctor who is not bookable — the seam that
    /// listing was built to expose.
    /// </remarks>
    [HttpGet("slots")]
    [ProducesResponseType(typeof(IReadOnlyList<PublicSlotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSlots(
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

        var result = await _slotService.GetAvailableAsync(doctorId, from, to, cancellationToken);

        return result switch
        {
            AvailableSlotsFoundResult found => Ok(found.Slots
                .Select(slot => new PublicSlotResponse(
                    slot.SlotId,
                    slot.StartUtc,
                    slot.EndUtc,
                    slot.SlotDate,
                    slot.DurationMinutes))
                .ToList()),
            AvailableSlotsDoctorNotFoundResult => DoctorNotFoundProblem(),
            _ => throw new InvalidOperationException("Unknown available slots result.")
        };
    }

    // ── GET /api/public/booking/days ──────────────────────────────────────────

    /// <summary>
    /// The doctor's clinic days, each marked full or not (SCRUM-37). <c>from</c> and <c>to</c> are
    /// Asia/Colombo dates, default to today through the slot horizon, and are clamped to it.
    /// </summary>
    /// <remarks>
    /// Lets the booking page tell a full day, which can be waited for, from a day the doctor does
    /// not work, which cannot — the free-slot listing alone shows both as empty. Says only whether
    /// a day is full: no counts, and nothing about who holds the times.
    /// </remarks>
    [HttpGet("days")]
    [ProducesResponseType(typeof(IReadOnlyList<PublicBookingDayResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDays(
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

        return await _waitlist.GetDaysAsync(doctorId, from, to, cancellationToken) switch
        {
            WaitlistDaysFoundResult found => Ok(found.Days),
            WaitlistDaysDoctorNotFoundResult => DoctorNotFoundProblem(),
            _ => throw new InvalidOperationException("Unknown waitlist days result.")
        };
    }
}
