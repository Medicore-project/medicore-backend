using System.Globalization;
using System.Text;
using Confluent.Kafka;
using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Interfaces;

namespace MediCore.Billing.Infrastructure.Messaging;

public sealed class KafkaEventPublisher : IKafkaEventPublisher
{
    private readonly IProducer<string, string> _producer;

    public KafkaEventPublisher(IProducer<string, string> producer)
    {
        _producer = producer;
    }

    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default) =>
        _producer.ProduceAsync(message.Topic, new Message<string, string>
        {
            Key = message.EventKey,
            Value = message.Payload,
            Headers =
            [
                Header("message-id", message.MessageId.ToString()),
                Header("correlation-id", message.CorrelationId),
                Header("occurred-at-utc", message.OccurredOnUtc.ToString("O", CultureInfo.InvariantCulture)),
                Header("event-type", message.EventType),
                Header("version", message.EventVersion.ToString(CultureInfo.InvariantCulture))
            ]
        }, cancellationToken);

    private static Header Header(string key, string value) =>
        new(key, Encoding.UTF8.GetBytes(value));
}
