namespace MediCore.Billing.Application.DTOs;

public sealed record InvoiceLineResponse(
    Guid InvoiceLineId,
    Guid? TariffId,
    string ServiceCode,
    string Description,
    int Quantity,
    decimal? UnitPrice,
    decimal LineTotal);

public sealed record PaymentResponse(
    Guid PaymentId,
    decimal Amount,
    string Method,
    DateTime RecordedAtUtc,
    string RecordedBy);

public sealed record RecordPaymentRequest(decimal Amount, string Method);

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
    decimal AmountPaid,
    decimal BalanceDue,
    bool RequiresManualPricing,
    string? PricingIssue,
    DateTime IssuedAtUtc,
    DateTime? FinalizedAtUtc,
    DateTime? VoidedAtUtc,
    string? VoidReason,
    DateTime? PaidAtUtc,
    IReadOnlyList<InvoiceLineResponse> Lines,
    IReadOnlyList<PaymentResponse> Payments);
