using System.Text.Json;
using MediCore.Billing.Application.Entities;
using MediCore.Contracts.Events.Billing;

namespace MediCore.Billing.Application.Messaging;

public static class BillingOutboxMessages
{
    public const string Topic = "billing-events";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static OutboxMessage InvoicePaid(
        Invoice invoice,
        Payment finalPayment,
        string correlationId,
        DateTime occurredAtUtc)
    {
        var paidEvent = new InvoicePaidEvent
        {
            InvoiceId = invoice.InvoiceId,
            PatientId = invoice.PatientId,
            Amount = invoice.Total,
            Method = finalPayment.Method,
            CorrelationId = correlationId,
            OccurredAtUtc = occurredAtUtc
        };

        return new OutboxMessage
        {
            MessageId = paidEvent.MessageId,
            Topic = Topic,
            EventKey = invoice.InvoiceId.ToString(),
            EventType = paidEvent.EventType,
            EventVersion = paidEvent.Version,
            CorrelationId = correlationId,
            Payload = JsonSerializer.Serialize(paidEvent, SerializerOptions),
            OccurredOnUtc = occurredAtUtc
        };
    }

    /// <summary>
    /// For a payment that leaves a balance. Read off the invoice after the payment has been
    /// applied to it, so the totals are the ones the patient should see on their receipt.
    /// </summary>
    public static OutboxMessage InvoicePartiallyPaid(
        Invoice invoice,
        Payment payment,
        string correlationId,
        DateTime occurredAtUtc)
    {
        var partiallyPaidEvent = new InvoicePartiallyPaidEvent
        {
            InvoiceId = invoice.InvoiceId,
            InvoiceNumber = invoice.InvoiceNumber,
            PatientId = invoice.PatientId,
            Amount = payment.Amount,
            Method = payment.Method,
            TotalPaid = invoice.AmountPaid,
            BalanceDue = invoice.Total - invoice.AmountPaid,
            Currency = invoice.Currency,
            CorrelationId = correlationId,
            OccurredAtUtc = occurredAtUtc
        };

        return new OutboxMessage
        {
            MessageId = partiallyPaidEvent.MessageId,
            Topic = Topic,
            // The invoice, as for invoice.paid, so one invoice's receipts stay in order.
            EventKey = invoice.InvoiceId.ToString(),
            EventType = partiallyPaidEvent.EventType,
            EventVersion = partiallyPaidEvent.Version,
            CorrelationId = correlationId,
            Payload = JsonSerializer.Serialize(partiallyPaidEvent, SerializerOptions),
            OccurredOnUtc = occurredAtUtc
        };
    }
}
