namespace MediCore.Identity.Application.Interfaces;

public interface IKafkaEventPublisher
{
    Task PublishAsync(
        string topic,
        string eventKey,
        string eventType,
        string payload,
        CancellationToken cancellationToken = default);
}