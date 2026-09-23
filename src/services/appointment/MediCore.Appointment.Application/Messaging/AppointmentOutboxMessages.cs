using System.Text.Json;
using MediCore.Appointment.Application.Entities;
using MediCore.Contracts.Events.Appointment;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Application.Messaging;

/// <summary>
/// Builds outbox rows for appointment-events.
/// </summary>
/// <remarks>
/// The row is written in the same transaction as the appointment it describes; a background
/// dispatcher drains it to Kafka afterwards. Nothing here touches a broker, which is what lets a
/// booking succeed while Kafka is down.
/// </remarks>
public static class AppointmentOutboxMessages
{
    public const string Topic = "appointment-events";

    /// <summary>
    /// camelCase, matching the Patient service's producer and the consumer that reads this topic.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// One <c>appointment.booked</c> for a confirmed booking, carrying the service code the billing
    /// service will invoice against (SCRUM-34 AC4).
    /// </summary>
    public static OutboxMessage Booked(
        AppointmentEntity appointment,
        string correlationId,
        DateTime occurredOnUtc)
    {
        var bookedEvent = new AppointmentBookedEvent
        {
            AppointmentId = appointment.AppointmentId,
            PatientId = appointment.PatientId,
            DoctorId = appointment.DoctorId,
            SlotStart = appointment.StartUtc,
            ServiceCode = appointment.ServiceCode,
            CorrelationId = correlationId,
            OccurredAtUtc = occurredOnUtc
        };

        return new OutboxMessage
        {
            // The event's own id, not a fresh one: it is written to a unique-indexed column and
            // travels as the message-id header consumers deduplicate on. The column has no default,
            // so leaving it unset would collide on the second row.
            MessageId = bookedEvent.MessageId,
            Topic = Topic,
            // The aggregate id becomes the Kafka partition key, so a later appointment.cancelled
            // lands on the same partition as this and ordering per appointment is guaranteed.
            EventKey = appointment.AppointmentId.ToString(),
            EventType = bookedEvent.EventType,
            EventVersion = bookedEvent.Version,
            CorrelationId = correlationId,
            Payload = JsonSerializer.Serialize(bookedEvent, SerializerOptions),
            OccurredOnUtc = occurredOnUtc
        };
    }
}
