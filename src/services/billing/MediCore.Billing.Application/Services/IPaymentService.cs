using MediCore.Billing.Application.DTOs;

namespace MediCore.Billing.Application.Services;

public interface IPaymentService
{
    Task<RecordPaymentResult> RecordAsync(
        Guid invoiceId,
        RecordPaymentRequest request,
        string recordedBy,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public abstract record RecordPaymentResult;
public sealed record PaymentRecordedResult(InvoiceResponse Invoice) : RecordPaymentResult;
public sealed record PaymentInvoiceNotFoundResult : RecordPaymentResult;
public sealed record PaymentInvoiceNotPayableResult(string Status) : RecordPaymentResult;
public sealed record PaymentOverpaymentResult(decimal BalanceDue) : RecordPaymentResult;
public sealed record PaymentConcurrentUpdateResult : RecordPaymentResult;
