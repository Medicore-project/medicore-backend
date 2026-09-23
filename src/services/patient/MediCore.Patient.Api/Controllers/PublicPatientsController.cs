using FluentValidation;
using MediCore.Patient.Api.Middleware;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediCore.Patient.Api.Controllers;

/// <summary>
/// The two anonymous endpoints the public booking page needs: identify yourself, or register as a
/// new patient. Both hand back a short-lived booking token.
/// </summary>
/// <remarks>
/// <para>
/// A separate controller from <see cref="PatientsController"/> on purpose. That one is
/// <c>[Authorize]</c> at the class level and every method carries a policy; dropping two
/// <c>[AllowAnonymous]</c> methods into it would leave an authorized controller quietly
/// half-anonymous, which no existing test would notice. Keeping the anonymous surface in its own
/// file makes it something a reviewer can see whole.
/// </para>
/// <para>
/// The gateway does not authenticate, so everything here is reachable from the public internet.
/// Nothing on it may reveal whether a given patient exists beyond what the caller already proved
/// they know.
/// </para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/patients")]
[EnableRateLimiting(PublicRateLimitPolicies.PublicBooking)]
public sealed class PublicPatientsController : ControllerBase
{
    /// <summary>
    /// The one body returned when identification fails, whatever the reason. It must stay a single
    /// shared constant: two messages that merely look similar would drift, and the difference
    /// would tell a guesser that a patient number exists but the date of birth was wrong.
    /// </summary>
    private const string IdentificationFailed =
        "We could not find a patient with that number and date of birth.";

    private readonly IValidator<IdentifyPatientRequest> _identifyValidator;
    private readonly IValidator<CreatePatientRequest> _registerValidator;
    private readonly IPatientIdentificationService _identificationService;
    private readonly IPatientRegistrationService _registrationService;
    private readonly ILogger<PublicPatientsController> _logger;

    public PublicPatientsController(
        IValidator<IdentifyPatientRequest> identifyValidator,
        IValidator<CreatePatientRequest> registerValidator,
        IPatientIdentificationService identificationService,
        IPatientRegistrationService registrationService,
        ILogger<PublicPatientsController> logger)
    {
        _identifyValidator = identifyValidator;
        _registerValidator = registerValidator;
        _identificationService = identificationService;
        _registrationService = registrationService;
        _logger = logger;
    }

    // ── POST /api/patients/identify ───────────────────────────────────────────

    /// <summary>
    /// Identifies a returning patient from their patient number and date of birth, returning a
    /// booking token.
    /// </summary>
    /// <remarks>
    /// Returns the same 404 for an unknown patient number as for a wrong date of birth.
    /// </remarks>
    [HttpPost("identify")]
    [ProducesResponseType(typeof(BookingIdentityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Identify(
        [FromBody] IdentifyPatientRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _identifyValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Identification validation failed.");
        }

        var identity = await _identificationService.IdentifyAsync(
            request.PatientNumber, request.DateOfBirth, cancellationToken);

        if (identity is null)
        {
            // Warning rather than Information: a burst of these from one source is what someone
            // working through patient numbers looks like in Seq.
            _logger.LogWarning(
                "Failed public patient identification for {PatientNumber} from {RemoteIp}.",
                request.PatientNumber,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            return NotFound(new ProblemDetails
            {
                Title = IdentificationFailed,
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(identity);
    }

    // ── POST /api/patients/public-register ────────────────────────────────────

    /// <summary>
    /// Registers a patient who is booking for the first time, returning their new patient number
    /// and a booking token.
    /// </summary>
    /// <remarks>
    /// Goes through the same registration service the front desk uses, so the patient row and its
    /// <c>patient.registered</c> event are written by one code path in one transaction.
    /// </remarks>
    [HttpPost("public-register")]
    [ProducesResponseType(typeof(BookingIdentityResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PublicRegister(
        [FromBody] CreatePatientRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _registerValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors, "Patient registration validation failed.");
        }

        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
            ?? Guid.NewGuid().ToString();

        var result = await _registrationService.RegisterAsync(
            request,
            correlationId,
            createdBy: "public-booking",
            cancellationToken);

        return result switch
        {
            PatientRegisteredResult registered => StatusCode(
                StatusCodes.Status201Created,
                _identificationService.IssueFor(
                    registered.Patient.PatientId,
                    registered.Patient.PatientNumber,
                    registered.Patient.FullName)),

            // Deliberately NOT the front desk's DuplicatePatientResponse, which carries the
            // existing patient's number, full name, email and archived flag. On an anonymous
            // endpoint that is an oracle: post a stolen NIC, learn a name and a patient number,
            // then use them to identify. The caller is told only that the NIC is taken.
            DuplicatePatientResult => Conflict(new ProblemDetails
            {
                Title = "A patient with this NIC is already registered. "
                    + "Use your patient number and date of birth to identify yourself.",
                Status = StatusCodes.Status409Conflict
            }),

            _ => throw new InvalidOperationException("Unknown patient registration result.")
        };
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
