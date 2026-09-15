using System.Security.Claims;
using FluentValidation;
using FluentValidation.Results;
using MediCore.Patient.Api.Authorization;
using MediCore.Patient.Api.Middleware;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Patient.Api.Controllers;

/// <summary>
/// Manages allergy records for an individual patient.
/// Active allergies are checked against new prescriptions at the point of care
/// to prevent dangerous drug interactions.
/// </summary>
[ApiController]
[Authorize]
[Route("api/patients/{patientId:guid}/allergies")]
public sealed class AllergiesController : ControllerBase
{
    private readonly IValidator<CreateAllergyRequest> _createValidator;
    private readonly IValidator<UpdateAllergyRequest> _updateValidator;
    private readonly IAllergyService _service;

    public AllergiesController(
        IValidator<CreateAllergyRequest> createValidator,
        IValidator<UpdateAllergyRequest> updateValidator,
        IAllergyService service)
    {
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _service = service;
    }

    // ── GET /api/patients/{patientId}/allergies ───────────────────────────────

    /// <summary>
    /// Returns all allergy records for a patient (active and inactive), ordered
    /// newest-first. Active allergies are displayed prominently at the point of care;
    /// inactive ones are retained for the full clinical history.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AllergyResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetListAsync(patientId, CreateAccessContext(), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    // ── GET /api/patients/{patientId}/allergies/{allergyId} ───────────────────

    /// <summary>Returns a single allergy by its stable business identifier.</summary>
    [HttpGet("{allergyId:guid}")]
    [ProducesResponseType(typeof(AllergyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(
        Guid patientId,
        Guid allergyId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(patientId, allergyId, CreateAccessContext(), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    // ── GET /api/patients/{patientId}/allergies/check?drug= ───────────────────

    /// <summary>
    /// Checks whether the supplied drug name conflicts with any active allergy
    /// for the patient using case-insensitive substring matching.
    /// Always returns 200 with a <see cref="AllergyConflictResponse"/>; inspect
    /// <c>HasConflict</c> to determine whether a warning should be shown.
    /// </summary>
    [HttpGet("check")]
    [ProducesResponseType(typeof(AllergyConflictResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CheckConflict(
        Guid patientId,
        [FromQuery] string drug,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(drug))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Drug name is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var result = await _service.CheckConflictAsync(patientId, drug.Trim(), cancellationToken);
        return Ok(result);
    }

    // ── POST /api/patients/{patientId}/allergies ──────────────────────────────

    /// <summary>Records a new active allergy against an existing patient.</summary>
    [HttpPost]
    [Authorize(Policy = PatientAuthorizationPolicies.ClinicalRecordWriter)]
    [ProducesResponseType(typeof(AllergyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        Guid patientId,
        [FromBody] CreateAllergyRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Allergy validation failed.");
        }

        var result = await _service.CreateAsync(patientId, request, CreateAccessContext(), cancellationToken);

        return result switch
        {
            AllergyCreatedResult created => CreatedAtAction(
                nameof(GetById),
                new { patientId, allergyId = created.Allergy.AllergyId },
                created.Allergy),
            AllergyCreatePatientNotFoundResult => NotFound(),
            _ => throw new InvalidOperationException("Unknown allergy creation result.")
        };
    }

    // ── PUT /api/patients/{patientId}/allergies/{allergyId} ───────────────────

    /// <summary>
    /// Updates the mutable fields (allergen, severity, reaction, notes) of an allergy.
    /// Returns 404 if the allergy does not exist or is soft-deleted.
    /// </summary>
    [HttpPut("{allergyId:guid}")]
    [Authorize(Policy = PatientAuthorizationPolicies.ClinicalRecordWriter)]
    [ProducesResponseType(typeof(AllergyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        Guid patientId,
        Guid allergyId,
        [FromBody] UpdateAllergyRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Allergy validation failed.");
        }

        var result = await _service.UpdateAsync(
            patientId, allergyId, request, CreateAccessContext(), cancellationToken);

        return result switch
        {
            AllergyUpdatedResult updated => Ok(updated.Allergy),
            AllergyUpdateNotFoundResult => NotFound(),
            _ => throw new InvalidOperationException("Unknown allergy update result.")
        };
    }

    // ── PATCH /api/patients/{patientId}/allergies/{allergyId}/deactivate ──────

    /// <summary>
    /// Transitions an active allergy to the Inactive state.
    /// Inactive allergies are excluded from prescription conflict checks.
    /// Returns 409 if the allergy is already inactive.
    /// </summary>
    [HttpPatch("{allergyId:guid}/deactivate")]
    [Authorize(Policy = PatientAuthorizationPolicies.ClinicalRecordWriter)]
    [ProducesResponseType(typeof(AllergyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Deactivate(
        Guid patientId,
        Guid allergyId,
        CancellationToken cancellationToken)
    {
        var result = await _service.DeactivateAsync(
            patientId, allergyId, CreateAccessContext(), cancellationToken);

        return result switch
        {
            AllergyDeactivatedResult deactivated => Ok(deactivated.Allergy),
            AllergyDeactivateNotFoundResult => NotFound(),
            AllergyDeactivateAlreadyInactiveResult => Conflict(new ProblemDetails
            {
                Title = "Allergy is already inactive.",
                Status = StatusCodes.Status409Conflict
            }),
            _ => throw new InvalidOperationException("Unknown allergy deactivate result.")
        };
    }

    // ── DELETE /api/patients/{patientId}/allergies/{allergyId} ───────────────

    /// <summary>
    /// Soft-deletes an allergy record. The record is retained in the database
    /// but excluded from all queries via the global query filter.
    /// Returns 404 when the allergy is not found.
    /// </summary>
    [HttpDelete("{allergyId:guid}")]
    [Authorize(Policy = PatientAuthorizationPolicies.ClinicalRecordWriter)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(
        Guid patientId,
        Guid allergyId,
        CancellationToken cancellationToken)
    {
        var deleted = await _service.DeleteAsync(
            patientId, allergyId, CreateAccessContext(), cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private PatientAccessContext CreateAccessContext()
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.Identity?.Name
            ?? "system";
        var actorRole = User.FindFirstValue(ClaimTypes.Role)
            ?? User.FindFirstValue("role")
            ?? "Unknown";
        var actorEmail = User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue("email");
        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
            ?? Guid.NewGuid().ToString();

        return new PatientAccessContext(
            actorId,
            actorRole,
            correlationId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            actorEmail);
    }

    private IActionResult CreateValidationProblem(
        IEnumerable<ValidationFailure> failures,
        string title)
    {
        var errors = failures
            .GroupBy(e => ToCamelCase(e.PropertyName))
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        return ValidationProblem(new ValidationProblemDetails(errors)
        {
            Title = title,
            Status = StatusCodes.Status400BadRequest
        });
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
