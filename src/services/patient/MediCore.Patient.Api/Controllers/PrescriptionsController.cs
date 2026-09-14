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
/// Manages prescriptions for an individual patient.
/// Active prescriptions represent current medication; completing a prescription
/// moves it to the read-only history list.
/// </summary>
[ApiController]
[Authorize]
[Route("api/patients/{patientId:guid}/prescriptions")]
public sealed class PrescriptionsController : ControllerBase
{
    private readonly IValidator<CreatePrescriptionRequest> _createValidator;
    private readonly IValidator<UpdatePrescriptionRequest> _updateValidator;
    private readonly IPrescriptionService _service;

    public PrescriptionsController(
        IValidator<CreatePrescriptionRequest> createValidator,
        IValidator<UpdatePrescriptionRequest> updateValidator,
        IPrescriptionService service)
    {
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _service = service;
    }

    // ── GET /api/patients/{patientId}/prescriptions ───────────────────────────

    /// <summary>
    /// Returns all prescriptions for a patient, split into active and history lists.
    /// Active prescriptions are current medication; history contains completed courses.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PrescriptionListResponse), StatusCodes.Status200OK)]
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

    // ── GET /api/patients/{patientId}/prescriptions/{prescriptionId} ──────────

    /// <summary>Returns a single prescription by its stable business identifier.</summary>
    [HttpGet("{prescriptionId:guid}")]
    [ProducesResponseType(typeof(PrescriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(
        Guid patientId,
        Guid prescriptionId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(patientId, prescriptionId, CreateAccessContext(), cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    // ── POST /api/patients/{patientId}/prescriptions ──────────────────────────

    /// <summary>Creates a new active prescription for the patient.</summary>
    [HttpPost]
    [Authorize(Policy = PatientAuthorizationPolicies.ClinicalRecordWriter)]
    [ProducesResponseType(typeof(PrescriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        Guid patientId,
        [FromBody] CreatePrescriptionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Prescription validation failed.");
        }

        var result = await _service.CreateAsync(patientId, request, CreateAccessContext(), cancellationToken);

        return result switch
        {
            PrescriptionCreatedResult created => CreatedAtAction(
                nameof(GetById),
                new { patientId, prescriptionId = created.Prescription.PrescriptionId },
                created.Prescription),
            PrescriptionCreatePatientNotFoundResult => NotFound(),
            _ => throw new InvalidOperationException("Unknown prescription creation result.")
        };
    }

    // ── PUT /api/patients/{patientId}/prescriptions/{prescriptionId} ──────────

    /// <summary>
    /// Updates the mutable fields of an active prescription.
    /// Returns 404 if the prescription does not exist or is already completed.
    /// </summary>
    [HttpPut("{prescriptionId:guid}")]
    [Authorize(Policy = PatientAuthorizationPolicies.ClinicalRecordWriter)]
    [ProducesResponseType(typeof(PrescriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        Guid patientId,
        Guid prescriptionId,
        [FromBody] UpdatePrescriptionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Prescription validation failed.");
        }

        var result = await _service.UpdateAsync(
            patientId, prescriptionId, request, CreateAccessContext(), cancellationToken);

        return result switch
        {
            PrescriptionUpdatedResult updated => Ok(updated.Prescription),
            PrescriptionUpdateNotFoundResult => NotFound(),
            _ => throw new InvalidOperationException("Unknown prescription update result.")
        };
    }

    // ── PATCH /api/patients/{patientId}/prescriptions/{prescriptionId}/complete

    /// <summary>
    /// Marks an active prescription as completed, moving it from the active list to history.
    /// Returns 409 Conflict if the prescription is already completed.
    /// </summary>
    [HttpPatch("{prescriptionId:guid}/complete")]
    [Authorize(Policy = PatientAuthorizationPolicies.ClinicalRecordWriter)]
    [ProducesResponseType(typeof(PrescriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Complete(
        Guid patientId,
        Guid prescriptionId,
        CancellationToken cancellationToken)
    {
        var result = await _service.CompleteAsync(
            patientId, prescriptionId, CreateAccessContext(), cancellationToken);

        return result switch
        {
            PrescriptionCompletedResult completed => Ok(completed.Prescription),
            PrescriptionCompleteNotFoundResult => NotFound(),
            PrescriptionCompleteAlreadyDoneResult => Conflict(new ProblemDetails
            {
                Title = "Prescription already completed.",
                Detail = "This prescription has already been marked as complete and is now historical.",
                Status = StatusCodes.Status409Conflict
            }),
            _ => throw new InvalidOperationException("Unknown prescription complete result.")
        };
    }

    // ── DELETE /api/patients/{patientId}/prescriptions/{prescriptionId} ───────

    /// <summary>Soft-deletes a prescription. The record is retained in the database.</summary>
    [HttpDelete("{prescriptionId:guid}")]
    [Authorize(Policy = PatientAuthorizationPolicies.ClinicalRecordWriter)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(
        Guid patientId,
        Guid prescriptionId,
        CancellationToken cancellationToken)
    {
        var deleted = await _service.DeleteAsync(
            patientId, prescriptionId, CreateAccessContext(), cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    // ── Private helpers (identical pattern to MedicalRecordsController) ───────

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
