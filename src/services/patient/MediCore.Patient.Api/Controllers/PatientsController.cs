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
    private readonly IPatientRegistrationService _registrationService;

    public PatientsController(
        IValidator<CreatePatientRequest> validator,
        IPatientRegistrationService registrationService)
    {
        _validator = validator;
        _registrationService = registrationService;
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
            var errors = validation.Errors
                .GroupBy(error => ToCamelCase(error.PropertyName))
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).Distinct().ToArray());

            return ValidationProblem(new ValidationProblemDetails(errors)
            {
                Title = "Patient registration validation failed.",
                Status = StatusCodes.Status400BadRequest
            });
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

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
