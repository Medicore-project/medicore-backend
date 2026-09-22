using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// Bookable doctors, from this service's own copy of Identity's staff data.
/// </summary>
/// <remarks>
/// Booking screens pick doctors here rather than from Identity, so booking keeps working while
/// Identity is down. Deliberate eventual consistency: a change made in Identity appears here a few
/// seconds later, once its staff event has been consumed.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/doctors")]
public sealed class DoctorsController : AppointmentControllerBase
{
    private readonly IDoctorDirectoryService _service;

    public DoctorsController(IDoctorDirectoryService service)
    {
        _service = service;
    }

    // ── GET /api/doctors ──────────────────────────────────────────────────────

    /// <summary>
    /// Every bookable doctor, ordered by name. <c>specialization</c> narrows to one specialization
    /// by whole name, ignoring case.
    /// </summary>
    /// <remarks>
    /// Deactivated doctors and staff who lost the Doctor role are left out. An empty list straight
    /// after deployment usually means the one-off backfill has not been run yet.
    /// </remarks>
    [HttpGet]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(IReadOnlyList<DoctorResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? specialization,
        CancellationToken cancellationToken) =>
        Ok(await _service.ListBookableAsync(specialization, cancellationToken));

    // ── GET /api/doctors/{doctorId} ───────────────────────────────────────────

    /// <summary>One bookable doctor. Returns 404 when the doctor is unknown or not bookable.</summary>
    [HttpGet("{doctorId:guid}")]
    [Authorize(Policy = AppointmentAuthorizationPolicies.ScheduleReader)]
    [ProducesResponseType(typeof(DoctorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(Guid doctorId, CancellationToken cancellationToken)
    {
        var doctor = await _service.GetBookableAsync(doctorId, cancellationToken);
        return doctor is null ? DoctorNotFoundProblem() : Ok(doctor);
    }
}
