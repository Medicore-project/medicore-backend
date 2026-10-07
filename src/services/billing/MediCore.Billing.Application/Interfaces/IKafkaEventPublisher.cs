using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Interfaces;

public interface IKafkaEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
