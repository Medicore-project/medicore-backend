using System.Text;
using Confluent.Kafka;
using MediCore.Identity.Application.Interfaces;

namespace MediCore.Identity.Infrastructure.Messaging;

public sealed class KafkaEventPublisher : IKafkaEventPublisher
{
    private readonly IProducer<string, string> _producer;

    public KafkaEventPublisher(IProducer<string, string> producer)
    {
        _producer = producer;
    }

    public async Task PublishAsync(
        string topic,
        string eventKey,
        string eventType,
        string payload,
        CancellationToken cancellationToken = default)
    {
        var message = new Message<string, string>
        {
            Key = eventKey,
            Value = payload,
            Headers =
            [
                new Header("event-type", Encoding.UTF8.GetBytes(eventType)),
                new Header("message-id", Encoding.UTF8.GetBytes(eventKey))
            ]
        };

        await _producer.ProduceAsync(topic, message, cancellationToken);
    }
}