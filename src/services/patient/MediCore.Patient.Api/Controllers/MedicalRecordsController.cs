using System.Security.Claims;
using FluentValidation;
using FluentValidation.Results;
using MediCore.Patient.Api.Middleware;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Patient.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/patients/{patientId:guid}/records")]
public sealed class MedicalRecordsController : ControllerBase
{
    private readonly IValidator<CreateMedicalRecordRequest> _createValidator;
    private readonly IValidator<UpdateMedicalRecordRequest> _updateValidator;
    private readonly IValidator<MedicalRecordListRequest> _listValidator;
    private readonly IMedicalRecordService _service;

    public MedicalRecordsController(
        IValidator<CreateMedicalRecordRequest> createValidator,
        IValidator<UpdateMedicalRecordRequest> updateValidator,
        IValidator<MedicalRecordListRequest> listValidator,
        IMedicalRecordService service)
    {
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _listValidator = listValidator;
        _service = service;
    }

    /// <summary>Gets the current medical record entries for an active patient.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedMedicalRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPage(
        Guid patientId,
        [FromQuery] MedicalRecordListRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _listValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Medical record query validation failed.");
        }

        var page = await _service.GetPageAsync(
            patientId,
            request,
            CreateAccessContext(),
            cancellationToken);

        return page is null ? NotFound() : Ok(page);
    }

    /// <summary>Gets the current version of a medical record entry.</summary>
    [HttpGet("{recordId:guid}")]
    [ProducesResponseType(typeof(MedicalRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(
        Guid patientId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        var record = await _service.GetByIdAsync(
            patientId,
            recordId,
            CreateAccessContext(),
            cancellationToken);

        return record is null ? NotFound() : Ok(record);
    }

    /// <summary>Gets every retained version of a medical record entry.</summary>
    [HttpGet("{recordId:guid}/versions")]
    [ProducesResponseType(typeof(IReadOnlyList<MedicalRecordResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetVersions(
        Guid patientId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        var versions = await _service.GetVersionsAsync(
            patientId,
            recordId,
            CreateAccessContext(),
            cancellationToken);

        return versions is null ? NotFound() : Ok(versions);
    }

    /// <summary>Creates a medical record entry for an active patient.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(MedicalRecordResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        Guid patientId,
        [FromBody] CreateMedicalRecordRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Medical record validation failed.");
        }

        var result = await _service.CreateAsync(
            patientId,
            request,
            CreateAccessContext(),
            cancellationToken);

        return result switch
        {
            MedicalRecordCreatedResult created => CreatedAtAction(
                nameof(GetById),
                new { patientId, recordId = created.Record.RecordId },
                created.Record),
            MedicalRecordCreatePatientNotFoundResult => NotFound(),
            _ => throw new InvalidOperationException("Unknown medical record creation result.")
        };
    }

    /// <summary>Creates a new version of an existing medical record entry.</summary>
    [HttpPut("{recordId:guid}")]
    [ProducesResponseType(typeof(MedicalRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        Guid patientId,
        Guid recordId,
        [FromBody] UpdateMedicalRecordRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Medical record validation failed.");
        }

        var result = await _service.UpdateAsync(
            patientId,
            recordId,
            request,
            CreateAccessContext(),
            cancellationToken);

        return result switch
        {
            MedicalRecordUpdatedResult updated => Ok(updated.Record),
            MedicalRecordUpdateNotFoundResult => NotFound(),
            MedicalRecordUpdateConflictResult conflict => Conflict(new ProblemDetails
            {
                Title = "Medical record version conflict.",
                Detail = $"The record has changed. The current version is {conflict.CurrentVersion}.",
                Status = StatusCodes.Status409Conflict
            }),
            _ => throw new InvalidOperationException("Unknown medical record update result.")
        };
    }

    /// <summary>Soft-deletes the current medical record entry.</summary>
    [HttpDelete("{recordId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(
        Guid patientId,
        Guid recordId,
        CancellationToken cancellationToken)
    {
        var deleted = await _service.DeleteAsync(
            patientId,
            recordId,
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
