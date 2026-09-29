namespace MediCore.Identity.Application.Interfaces;

public interface IKafkaEventPublisher
{
    /// <param name="messageId">Unique per message; the outbox row's id. Never the event key.</param>
    Task PublishAsync(
        Guid messageId,
        string topic,
        string eventKey,
        string eventType,
        string payload,
        CancellationToken cancellationToken = default);
}