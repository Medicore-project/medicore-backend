using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>
/// Puts one outbox row on its topic. Only the outbox dispatcher calls this — application services
/// never publish inside an HTTP request.
/// </summary>
public interface IKafkaEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
