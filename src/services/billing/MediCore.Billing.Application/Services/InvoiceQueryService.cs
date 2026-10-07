using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;

namespace MediCore.Billing.Application.Services;

public sealed class InvoiceQueryService : IInvoiceQueryService
{
    private readonly IInvoiceRepository _invoiceRepository;

    public InvoiceQueryService(IInvoiceRepository invoiceRepository)
    {
        _invoiceRepository = invoiceRepository;
    }

    public async Task<InvoiceResponse?> GetByIdAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default) =>
        Map(await _invoiceRepository.GetByIdAsync(invoiceId, cancellationToken));

    public async Task<InvoiceResponse?> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default) =>
        Map(await _invoiceRepository.GetByAppointmentIdAsync(appointmentId, cancellationToken));

    private static InvoiceResponse? Map(Invoice? invoice) =>
        invoice is null ? null : InvoiceMapping.ToResponse(invoice);
}
