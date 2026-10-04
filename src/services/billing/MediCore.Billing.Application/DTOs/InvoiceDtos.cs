namespace MediCore.Billing.Application.DTOs;

public sealed record InvoiceLineResponse(
    Guid InvoiceLineId,
    Guid? TariffId,
    string ServiceCode,
    string Description,
    int Quantity,
    decimal? UnitPrice,
    decimal LineTotal);

public sealed record InvoiceResponse(
    Guid InvoiceId,
    string InvoiceNumber,
    Guid AppointmentId,
    Guid PatientId,
    string ServiceCode,
    string Status,
    string Currency,
    decimal Subtotal,
    decimal Total,
    bool RequiresManualPricing,
    string? PricingIssue,
    DateTime IssuedAtUtc,
    DateTime? FinalizedAtUtc,
    IReadOnlyList<InvoiceLineResponse> Lines);
