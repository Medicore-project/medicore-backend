using FluentValidation;
using FluentValidation.Results;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Billing.Api.Controllers;

[ApiController]
[Route("api/service-tariffs")]
[Authorize(Roles = "Admin,Receptionist")]
public sealed class ServiceTariffsController : ControllerBase
{
    private readonly IServiceTariffService _service;
    private readonly IValidator<CreateServiceTariffRequest> _createValidator;
    private readonly IValidator<UpdateServiceTariffRequest> _updateValidator;

    public ServiceTariffsController(
        IServiceTariffService service,
        IValidator<CreateServiceTariffRequest> createValidator,
        IValidator<UpdateServiceTariffRequest> updateValidator)
    {
        _service = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ServiceTariffResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ServiceTariffResponse>>> GetAll(
        [FromQuery] bool includeInactive = true,
        CancellationToken cancellationToken = default) =>
        Ok(await _service.GetAllAsync(includeInactive, cancellationToken));

    [HttpGet("{tariffId:guid}")]
    [ProducesResponseType<ServiceTariffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceTariffResponse>> GetById(
        Guid tariffId,
        CancellationToken cancellationToken)
    {
        var tariff = await _service.GetByIdAsync(tariffId, cancellationToken);
        return tariff is null ? NotFound() : Ok(tariff);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<ServiceTariffResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateServiceTariffRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation.Errors, "Tariff validation failed.");
        }

        var result = await _service.CreateAsync(request, cancellationToken);
        return result switch
        {
            ServiceTariffCreatedResult created => CreatedAtAction(
                nameof(GetById),
                new { tariffId = created.Tariff.TariffId },
                created.Tariff),
            DuplicateServiceTariffCodeResult duplicate => Conflict(new ProblemDetails
            {
                Title = $"Service tariff code '{duplicate.ServiceCode}' already exists.",
                Status = StatusCodes.Status409Conflict
            }),
            _ => throw new InvalidOperationException("Unknown tariff creation result.")
        };
    }

    [HttpPut("{tariffId:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<ServiceTariffResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdatePrice(
        Guid tariffId,
        [FromBody] UpdateServiceTariffRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation.Errors, "Tariff validation failed.");
        }

        var result = await _service.UpdatePriceAsync(tariffId, request, cancellationToken);
        return result switch
        {
            ServiceTariffVersionCreatedResult updated => Ok(updated.Tariff),
            ServiceTariffNotFoundResult => NotFound(new ProblemDetails
            {
                Title = "Service tariff not found.",
                Status = StatusCodes.Status404NotFound
            }),
            ServiceTariffInactiveResult => Conflict(new ProblemDetails
            {
                Title = "An inactive service tariff cannot receive a new price.",
                Status = StatusCodes.Status409Conflict
            }),
            ServiceTariffEffectiveDateConflictResult conflict => Conflict(new ProblemDetails
            {
                Title = $"The new effective date must be after {conflict.LatestEffectiveFromUtc:O}.",
                Status = StatusCodes.Status409Conflict
            }),
            _ => throw new InvalidOperationException("Unknown tariff update result.")
        };
    }

    [HttpDelete("{tariffId:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(
        Guid tariffId,
        CancellationToken cancellationToken) =>
        await _service.DeactivateAsync(tariffId, cancellationToken) == DeactivateServiceTariffResult.NotFound
            ? NotFound()
            : NoContent();

    private IActionResult ValidationFailure(IEnumerable<ValidationFailure> failures, string title)
    {
        var errors = failures
            .GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..])
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).Distinct().ToArray());

        return ValidationProblem(new ValidationProblemDetails(errors)
        {
            Title = title,
            Status = StatusCodes.Status400BadRequest
        });
    }
}
