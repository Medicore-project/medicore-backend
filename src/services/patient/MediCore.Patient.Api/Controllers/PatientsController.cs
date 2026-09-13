using System.Security.Claims;
using FluentValidation;
using MediCore.Patient.Api.Middleware;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Patient.Api.Controllers;

[ApiController]
[Authorize(Policy = "FrontDesk")]
[Route("api/patients")]
public sealed class PatientsController : ControllerBase
{
    private readonly IValidator<CreatePatientRequest> _validator;
    private readonly IValidator<UpdatePatientRequest> _updateValidator;
    private readonly IValidator<PatientSearchRequest> _searchValidator;
    private readonly IPatientRegistrationService _registrationService;
    private readonly IPatientProfileService _profileService;
    private readonly IPatientSearchService _searchService;

    public PatientsController(
        IValidator<CreatePatientRequest> validator,
        IValidator<UpdatePatientRequest> updateValidator,
        IValidator<PatientSearchRequest> searchValidator,
        IPatientRegistrationService registrationService,
        IPatientProfileService profileService,
        IPatientSearchService searchService)
    {
        _validator = validator;
        _updateValidator = updateValidator;
        _searchValidator = searchValidator;
        _registrationService = registrationService;
        _profileService = profileService;
        _searchService = searchService;
    }

    /// <summary>Registers a new patient and schedules a patient.registered event.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(PatientRegistrationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(DuplicatePatientResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Register(
        [FromBody] CreatePatientRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Patient registration validation failed.");
        }

        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
            ?? Guid.NewGuid().ToString();
        var createdBy = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.Identity?.Name
            ?? "system";

        var result = await _registrationService.RegisterAsync(
            request,
            correlationId,
            createdBy,
            cancellationToken);

        return result switch
        {
            PatientRegisteredResult registered => Created(
                $"/api/patients/{registered.Patient.PatientId}",
                registered.Patient),
            DuplicatePatientResult duplicate => Conflict(new DuplicatePatientResponse(
                "A patient with this NIC is already registered.",
                duplicate.ExistingPatient)),
            _ => throw new InvalidOperationException("Unknown patient registration result.")
        };
    }

    /// <summary>Searches active patients by name, NIC or patient number.</summary>
    /// <remarks>
    /// Results are paginated and ordered with exact NIC or patient-number matches first.
    /// A blank query or a query with no matches returns an empty page.
    /// </remarks>
    [HttpGet("search")]
    [ProducesResponseType(typeof(PatientSearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Search(
        [FromQuery] PatientSearchRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _searchValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Patient search validation failed.");
        }

        var result = await _searchService.SearchAsync(
            request,
            CreateAccessContext(),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Gets an active patient profile and records the access in the audit log.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PatientProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var patient = await _profileService.GetByIdAsync(
            id,
            CreateAccessContext(),
            cancellationToken);

        return patient is null ? NotFound() : Ok(patient);
    }

    /// <summary>Updates an active patient's personal and contact details.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PatientProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdatePatientRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Patient profile validation failed.");
        }

        var result = await _profileService.UpdateAsync(
            id,
            request,
            CreateAccessContext(),
            cancellationToken);

        return result switch
        {
            PatientUpdatedResult updated => Ok(updated.Patient),
            PatientUpdateNotFoundResult => NotFound(),
            _ => throw new InvalidOperationException("Unknown patient update result.")
        };
    }

    /// <summary>Soft-deletes an active patient profile.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _profileService.DeleteAsync(
            id,
            CreateAccessContext(),
            cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    private PatientAccessContext CreateAccessContext()
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.Identity?.Name
            ?? "system";
        var actorRole = User.FindFirstValue(ClaimTypes.Role)
            ?? User.FindFirstValue("role")
            ?? "Unknown";
        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
            ?? Guid.NewGuid().ToString();

        return new PatientAccessContext(
            actorId,
            actorRole,
            correlationId,
            HttpContext.Connection.RemoteIpAddress?.ToString());
    }

    private IActionResult CreateValidationProblem(
        IEnumerable<FluentValidation.Results.ValidationFailure> failures,
        string title)
    {
        var errors = failures
            .GroupBy(error => ToCamelCase(error.PropertyName))
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).Distinct().ToArray());

        return ValidationProblem(new ValidationProblemDetails(errors)
        {
            Title = title,
            Status = StatusCodes.Status400BadRequest
        });
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
