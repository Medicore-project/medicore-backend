using System.Text.Json;
using MediCore.Billing.Application.Services;
using MediCore.Contracts.Events.Appointment;
using MediCore.Contracts.Events.Billing;
using MediCore.Contracts.Events.Patient;

namespace MediCore.Billing.Infrastructure.Messaging;

public interface INotificationEventProcessor
{
    Task<NotificationEventProcessingResult> ProcessAsync(string? eventType, string payload, CancellationToken cancellationToken);
}

public enum NotificationEventProcessingResult
{
    Ignored,
    Sent,
    Duplicate
}

public sealed class NotificationEventProcessor : INotificationEventProcessor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly INotificationService _notifications;

    public NotificationEventProcessor(INotificationService notifications)
    {
        _notifications = notifications;
    }

    public async Task<NotificationEventProcessingResult> ProcessAsync(
        string? eventType,
        string payload,
        CancellationToken cancellationToken)
    {
        NotificationDeliveryResult? result = eventType?.Trim().ToLowerInvariant() switch
        {
            "patient.registered" => await _notifications.HandlePatientRegisteredAsync(
                Deserialize<PatientRegisteredEvent>(payload), cancellationToken),
            "appointment.booked" => await _notifications.HandleAppointmentBookedAsync(
                Deserialize<AppointmentBookedEvent>(payload), cancellationToken),
            "invoice.paid" => await _notifications.HandleInvoicePaidAsync(
                Deserialize<InvoicePaidEvent>(payload), cancellationToken),
            _ => null
        };

        return result switch
        {
            NotificationDeliveryResult.Sent => NotificationEventProcessingResult.Sent,
            NotificationDeliveryResult.Duplicate => NotificationEventProcessingResult.Duplicate,
            _ => NotificationEventProcessingResult.Ignored
        };
    }

    private static TEvent Deserialize<TEvent>(string payload) where TEvent : class =>
        JsonSerializer.Deserialize<TEvent>(payload, SerializerOptions)
        ?? throw new JsonException($"The {typeof(TEvent).Name} payload is empty.");
}
