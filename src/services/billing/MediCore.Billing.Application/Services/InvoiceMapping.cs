using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Services;

public static class InvoiceMapping
{
    public static InvoiceResponse ToResponse(Invoice invoice) => new(
        invoice.InvoiceId,
        invoice.InvoiceNumber,
        invoice.AppointmentId,
        invoice.PatientId,
        invoice.ServiceCode,
        invoice.Status,
        invoice.Currency,
        invoice.Subtotal,
        invoice.Total,
        invoice.AmountPaid,
        Math.Max(0m, invoice.Total - invoice.AmountPaid),
        invoice.RequiresManualPricing,
        invoice.PricingIssue,
        invoice.IssuedAtUtc,
        invoice.FinalizedAtUtc,
        invoice.VoidedAtUtc,
        invoice.VoidReason,
        invoice.PaidAtUtc,
        invoice.Lines.Select(line => new InvoiceLineResponse(
            line.InvoiceLineId,
            line.TariffId,
            line.ServiceCode,
            line.Description,
            line.Quantity,
            line.UnitPrice,
            line.LineTotal)).ToArray(),
        invoice.Payments
            .OrderBy(payment => payment.RecordedAtUtc)
            .Select(payment => new PaymentResponse(
                payment.PaymentId,
                payment.Amount,
                payment.Method,
                payment.RecordedAtUtc,
                payment.RecordedBy))
            .ToArray());
}
