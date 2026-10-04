using System.Text.Json;
using MediCore.Billing.Application.Services;
using MediCore.Contracts.Events.Appointment;

namespace MediCore.Billing.Infrastructure.Messaging;

public interface IAppointmentEventProcessor
{
    Task<AppointmentEventProcessingResult> ProcessAsync(
        string? eventType,
        string payload,
        CancellationToken cancellationToken);
}

public enum AppointmentEventProcessingResult
{
    Ignored,
    Processed,
    MissingTariff,
    Duplicate,
    InvoiceAlreadyExists,
    InvoiceNotFound,
    AlreadyFinalized
}

public sealed class AppointmentEventProcessor : IAppointmentEventProcessor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IAppointmentBillingHandler _handler;

    public AppointmentEventProcessor(IAppointmentBillingHandler handler)
    {
        _handler = handler;
    }

    public async Task<AppointmentEventProcessingResult> ProcessAsync(
        string? eventType,
        string payload,
        CancellationToken cancellationToken)
    {
        AppointmentBillingResult? result = eventType?.ToLowerInvariant() switch
        {
            "appointment.booked" => await _handler.HandleBookedAsync(
                Deserialize<AppointmentBookedEvent>(payload), cancellationToken),
            "appointment.completed" => await _handler.HandleCompletedAsync(
                Deserialize<AppointmentCompletedEvent>(payload), cancellationToken),
            _ => null
        };

        return result is null
            ? AppointmentEventProcessingResult.Ignored
            : Enum.Parse<AppointmentEventProcessingResult>(result.Value.ToString());
    }

    private static TEvent Deserialize<TEvent>(string payload) where TEvent : class =>
        JsonSerializer.Deserialize<TEvent>(payload, SerializerOptions)
        ?? throw new JsonException($"The {typeof(TEvent).Name} payload is empty.");
}
