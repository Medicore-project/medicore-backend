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

    private static InvoiceResponse? Map(Invoice? invoice) => invoice is null
        ? null
        : new InvoiceResponse(
            invoice.InvoiceId,
            invoice.InvoiceNumber,
            invoice.AppointmentId,
            invoice.PatientId,
            invoice.ServiceCode,
            invoice.Status,
            invoice.Currency,
            invoice.Subtotal,
            invoice.Total,
            invoice.RequiresManualPricing,
            invoice.PricingIssue,
            invoice.IssuedAtUtc,
            invoice.FinalizedAtUtc,
            invoice.Lines.Select(line => new InvoiceLineResponse(
                line.InvoiceLineId,
                line.TariffId,
                line.ServiceCode,
                line.Description,
                line.Quantity,
                line.UnitPrice,
                line.LineTotal)).ToArray());
}
