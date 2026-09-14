using System.Text.Json;
using MediCore.Contracts.Events.Appointment;
using MediCore.Patient.Application.Services;

namespace MediCore.Patient.Infrastructure.Messaging;

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
    Duplicate,
    DeadLetterQueued
}

public sealed class AppointmentEventProcessor : IAppointmentEventProcessor
{
    private const string CompletedEventType = "appointment.completed";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IAppointmentCompletedHandler _handler;

    public AppointmentEventProcessor(IAppointmentCompletedHandler handler)
    {
        _handler = handler;
    }

    public async Task<AppointmentEventProcessingResult> ProcessAsync(
        string? eventType,
        string payload,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(eventType, CompletedEventType, StringComparison.OrdinalIgnoreCase))
        {
            return AppointmentEventProcessingResult.Ignored;
        }

        var completedEvent = JsonSerializer.Deserialize<AppointmentCompletedEvent>(payload, SerializerOptions)
            ?? throw new JsonException("The appointment.completed event payload is empty.");

        var result = await _handler.HandleAsync(completedEvent, cancellationToken);
        return result switch
        {
            AppointmentCompletedHandlingResult.Processed => AppointmentEventProcessingResult.Processed,
            AppointmentCompletedHandlingResult.Duplicate => AppointmentEventProcessingResult.Duplicate,
            AppointmentCompletedHandlingResult.DeadLetterQueued => AppointmentEventProcessingResult.DeadLetterQueued,
            _ => throw new InvalidOperationException($"Unsupported appointment handling result '{result}'.")
        };
    }
}
