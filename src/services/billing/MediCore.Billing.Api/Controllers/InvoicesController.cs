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

    public InvoicesController(IInvoiceQueryService queryService)
    {
        _queryService = queryService;
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
}
