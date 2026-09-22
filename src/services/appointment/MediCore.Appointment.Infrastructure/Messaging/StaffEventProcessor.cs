using System.Text.Json;
using MediCore.Appointment.Application.Services;
using MediCore.Contracts.Events.Staff;
using Microsoft.Extensions.Logging;

namespace MediCore.Appointment.Infrastructure.Messaging;

public interface IStaffEventProcessor
{
    Task<StaffEventResult> ProcessAsync(
        string? eventType,
        string? payload,
        CancellationToken cancellationToken);
}

/// <summary>
/// Turns one raw staff-events message into a typed event and hands it to
/// <see cref="IStaffEventHandler"/>.
/// </summary>
public sealed class StaffEventProcessor : IStaffEventProcessor
{
    public const string CreatedEventType = "staff.created";
    public const string UpdatedEventType = "staff.updated";
    public const string DeactivatedEventType = "staff.deactivated";

    // Case-insensitive property names: Identity serializes with default (PascalCase) options.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IStaffEventHandler _handler;
    private readonly ILogger<StaffEventProcessor> _logger;

    public StaffEventProcessor(IStaffEventHandler handler, ILogger<StaffEventProcessor> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    public async Task<StaffEventResult> ProcessAsync(
        string? eventType,
        string? payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            _logger.LogWarning("Rejecting a staff-events message with no event type.");
            return new StaffEventRejectedResult("MissingEventType");
        }

        switch (eventType.Trim().ToLowerInvariant())
        {
            case CreatedEventType:
                var created = Deserialize<StaffCreatedEvent>(eventType, payload);
                return created is null
                    ? new StaffEventRejectedResult("MalformedPayload")
                    : await _handler.HandleCreatedAsync(created, cancellationToken);

            case UpdatedEventType:
                var updated = Deserialize<StaffUpdatedEvent>(eventType, payload);
                return updated is null
                    ? new StaffEventRejectedResult("MalformedPayload")
                    : await _handler.HandleUpdatedAsync(updated, cancellationToken);

            case DeactivatedEventType:
                var deactivated = Deserialize<StaffDeactivatedEvent>(eventType, payload);
                return deactivated is null
                    ? new StaffEventRejectedResult("MalformedPayload")
                    : await _handler.HandleDeactivatedAsync(deactivated, cancellationToken);

            default:
                // Staff events this service has no use for; not an error.
                return new StaffEventIgnoredResult("UnhandledEventType");
        }
    }

    /// <summary>
    /// Null for a payload that is empty, not JSON, or missing a required field. Such a message fails
    /// identically on every retry, so it is skipped rather than thrown. It is not written to the
    /// processed-message log — without a readable MessageId there is nothing to key it on — so
    /// committing its offset is what stops it coming back.
    /// </summary>
    private T? Deserialize<T>(string eventType, string? payload) where T : class
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            _logger.LogWarning("Rejecting {EventType}: the message has no payload.", eventType);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, SerializerOptions);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Rejecting {EventType}: the payload could not be read.", eventType);
            return null;
        }
    }
}
