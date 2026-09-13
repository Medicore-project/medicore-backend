using MediCore.Patient.Application.Entities;

namespace MediCore.Patient.Application.Interfaces;

public interface IKafkaEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
