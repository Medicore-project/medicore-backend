using System.Security.Claims;
using FluentValidation;
using FluentValidation.Results;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Billing.Api.Controllers;

[ApiController]
[Route("api/invoices")]
[Authorize(Roles = "Admin,Receptionist")]
public sealed class InvoicesController : ControllerBase
{
    private readonly IInvoiceQueryService _queryService;
    private readonly IPaymentService _paymentService;
    private readonly IValidator<RecordPaymentRequest> _paymentValidator;

    public InvoicesController(
        IInvoiceQueryService queryService,
        IPaymentService paymentService,
        IValidator<RecordPaymentRequest> paymentValidator)
    {
        _queryService = queryService;
        _paymentService = paymentService;
        _paymentValidator = paymentValidator;
    }

    [HttpGet("{invoiceId:guid}")]
    [ProducesResponseType<InvoiceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceResponse>> GetById(
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        var invoice = await _queryService.GetByIdAsync(invoiceId, cancellationToken);
        return invoice is null ? NotFound() : Ok(invoice);
    }

    [HttpGet("by-appointment/{appointmentId:guid}")]
    [ProducesResponseType<InvoiceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceResponse>> GetByAppointment(
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var invoice = await _queryService.GetByAppointmentIdAsync(appointmentId, cancellationToken);
        return invoice is null ? NotFound() : Ok(invoice);
    }

    [HttpPost("{invoiceId:guid}/payments")]
    [ProducesResponseType<InvoiceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordPayment(
        Guid invoiceId,
        [FromBody] RecordPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _paymentValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return CreateValidationProblem(validation.Errors);
        }

        var result = await _paymentService.RecordAsync(
            invoiceId,
            request,
            CurrentActor(),
            HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString(),
            cancellationToken);

        return result switch
        {
            PaymentRecordedResult recorded => Ok(recorded.Invoice),
            PaymentInvoiceNotFoundResult => NotFound(new ProblemDetails
            {
                Title = "Invoice not found.",
                Status = StatusCodes.Status404NotFound
            }),
            PaymentInvoiceNotPayableResult notPayable => Conflict(new ProblemDetails
            {
                Title = $"Payments cannot be recorded while the invoice is {notPayable.Status}.",
                Status = StatusCodes.Status409Conflict
            }),
            PaymentOverpaymentResult overpayment => BadRequest(new ProblemDetails
            {
                Title = "Payment exceeds the outstanding balance.",
                Detail = $"The outstanding balance is {overpayment.BalanceDue:0.00}.",
                Status = StatusCodes.Status400BadRequest
            }),
            PaymentConcurrentUpdateResult => Conflict(new ProblemDetails
            {
                Title = "The invoice balance changed. Reload it before recording another payment.",
                Status = StatusCodes.Status409Conflict
            }),
            _ => throw new InvalidOperationException("Unknown payment result.")
        };
    }

    private string CurrentActor() =>
        User.FindFirstValue(ClaimTypes.Email)
        ?? User.FindFirstValue("email")
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? User.Identity?.Name
        ?? "system";

    private IActionResult CreateValidationProblem(IEnumerable<ValidationFailure> failures)
    {
        var errors = failures
            .GroupBy(error => char.ToLowerInvariant(error.PropertyName[0]) + error.PropertyName[1..])
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).Distinct().ToArray());

        return ValidationProblem(new ValidationProblemDetails(errors)
        {
            Title = "Payment validation failed.",
            Status = StatusCodes.Status400BadRequest
        });
    }
}
