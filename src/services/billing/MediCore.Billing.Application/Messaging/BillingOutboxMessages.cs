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
}
