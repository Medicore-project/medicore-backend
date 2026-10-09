using FluentValidation;
using FluentValidation.Results;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Billing.Api.Controllers;

[ApiController]
[Route("api/notification-templates")]
[Authorize(Roles = "Admin")]
public sealed class NotificationTemplatesController : ControllerBase
{
    private readonly INotificationTemplateService _service;
    private readonly IValidator<CreateNotificationTemplateRequest> _createValidator;
    private readonly IValidator<UpdateNotificationTemplateRequest> _updateValidator;

    public NotificationTemplatesController(
        INotificationTemplateService service,
        IValidator<CreateNotificationTemplateRequest> createValidator,
        IValidator<UpdateNotificationTemplateRequest> updateValidator)
    {
        _service = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NotificationTemplateResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NotificationTemplateResponse>>> GetAll(
        [FromQuery] bool includeInactive = true,
        CancellationToken cancellationToken = default) =>
        Ok(await _service.GetAllAsync(includeInactive, cancellationToken));

    [HttpGet("{templateId:guid}")]
    [ProducesResponseType<NotificationTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationTemplateResponse>> GetById(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var template = await _service.GetByIdAsync(templateId, cancellationToken);
        return template is null ? NotFound() : Ok(template);
    }

    [HttpPost]
    [ProducesResponseType<NotificationTemplateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateNotificationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);

        var result = await _service.CreateAsync(request, cancellationToken);
        return result switch
        {
            NotificationTemplateCreatedResult created => CreatedAtAction(
                nameof(GetById),
                new { templateId = created.Template.NotificationTemplateId },
                created.Template),
            DuplicateNotificationTemplateCodeResult duplicate => Conflict(new ProblemDetails
            {
                Title = $"Notification template code '{duplicate.Code}' already exists.",
                Status = StatusCodes.Status409Conflict
            }),
            _ => throw new InvalidOperationException("Unknown notification template result.")
        };
    }

    [HttpPut("{templateId:guid}")]
    [ProducesResponseType<NotificationTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid templateId,
        [FromBody] UpdateNotificationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);

        var result = await _service.UpdateAsync(templateId, request, cancellationToken);
        return result switch
        {
            NotificationTemplateUpdatedResult updated => Ok(updated.Template),
            NotificationTemplateNotFoundResult => NotFound(),
            _ => throw new InvalidOperationException("Unknown notification template result.")
        };
    }

    [HttpDelete("{templateId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid templateId, CancellationToken cancellationToken) =>
        await _service.DeactivateAsync(templateId, cancellationToken) ? NoContent() : NotFound();

    private IActionResult ValidationFailure(IEnumerable<ValidationFailure> failures)
    {
        var errors = failures
            .GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..])
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).Distinct().ToArray());
        return ValidationProblem(new ValidationProblemDetails(errors)
        {
            Title = "Notification template validation failed.",
            Status = StatusCodes.Status400BadRequest
        });
    }
}

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = "Admin,Receptionist")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationTemplateService _service;

    public NotificationsController(INotificationTemplateService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NotificationLogResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NotificationLogResponse>>> GetRecent(
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await _service.GetRecentLogsAsync(limit, cancellationToken));
}
