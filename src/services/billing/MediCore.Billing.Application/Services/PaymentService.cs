using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Exceptions;
using MediCore.Billing.Application.Interfaces;
using MediCore.Billing.Application.Messaging;

namespace MediCore.Billing.Application.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOutboxMessageRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IOutboxMessageRepository outboxRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _paymentRepository = paymentRepository;
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<RecordPaymentResult> RecordAsync(
        Guid invoiceId,
        RecordPaymentRequest request,
        string recordedBy,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _paymentRepository.GetInvoiceForPaymentAsync(invoiceId, cancellationToken);
        if (invoice is null)
        {
            return new PaymentInvoiceNotFoundResult();
        }

        if (!string.Equals(invoice.Status, InvoiceStatus.Payable, StringComparison.Ordinal))
        {
            return new PaymentInvoiceNotPayableResult(invoice.Status);
        }

        var method = PaymentMethods.Normalize(request.Method)
            ?? throw new ArgumentException("Unsupported payment method.", nameof(request));
        if (request.Amount <= 0m)
        {
            throw new ArgumentException("Payment amount must be greater than zero.", nameof(request));
        }

        var balanceDue = invoice.Total - invoice.AmountPaid;
        if (request.Amount > balanceDue)
        {
            return new PaymentOverpaymentResult(balanceDue);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var payment = new Payment
        {
            InvoiceId = invoice.InvoiceId,
            Amount = request.Amount,
            Method = method,
            RecordedAtUtc = nowUtc,
            RecordedBy = string.IsNullOrWhiteSpace(recordedBy) ? "system" : recordedBy.Trim()
        };

        await _paymentRepository.AddAsync(payment, cancellationToken);
        invoice.Payments.Add(payment);
        invoice.AmountPaid += payment.Amount;
        invoice.UpdatedAtUtc = nowUtc;
        invoice.Version++;

        if (invoice.AmountPaid == invoice.Total)
        {
            invoice.Status = InvoiceStatus.Paid;
            invoice.PaidAtUtc = nowUtc;
            await _outboxRepository.AddAsync(
                BillingOutboxMessages.InvoicePaid(invoice, payment, correlationId, nowUtc),
                cancellationToken);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return new PaymentRecordedResult(InvoiceMapping.ToResponse(invoice));
        }
        catch (ConcurrentPaymentException)
        {
            return new PaymentConcurrentUpdateResult();
        }
    }
}
