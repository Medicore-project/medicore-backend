using System.Text.Json;
using MediCore.Appointment.Application.Entities;
using MediCore.Contracts.Events;
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

    /// <summary>
    /// One <c>appointment.cancelled</c> for a cancellation (SCRUM-36), so billing can void what it
    /// raised for the booking.
    /// </summary>
    /// <remarks>
    /// Keyed by the appointment id exactly as <see cref="Booked"/> is. That shared key puts both on
    /// one partition, which is what stops a cancellation reaching billing before the booking it
    /// cancels.
    /// </remarks>
    public static OutboxMessage Cancelled(
        AppointmentEntity appointment,
        string reason,
        string correlationId,
        DateTime occurredOnUtc)
    {
        var cancelledEvent = new AppointmentCancelledEvent
        {
            AppointmentId = appointment.AppointmentId,
            Reason = reason,
            CorrelationId = correlationId,
            OccurredAtUtc = occurredOnUtc
        };

        return ToOutboxMessage(
            cancelledEvent,
            appointment,
            JsonSerializer.Serialize(cancelledEvent, SerializerOptions),
            correlationId,
            occurredOnUtc);
    }

    /// <summary>
    /// One <c>appointment.completed</c> for a finished visit (SCRUM-36). The Patient service turns
    /// it into a medical record entry, using <paramref name="notes"/> as the clinical notes and the
    /// appointment id as the visit reference.
    /// </summary>
    /// <remarks>
    /// That consumer dead-letters an event whose notes are blank or longer than 8000 characters,
    /// or whose version is not 1, so the request validator enforces the same limits before the
    /// row is ever written.
    /// </remarks>
    public static OutboxMessage Completed(
        AppointmentEntity appointment,
        string notes,
        string correlationId,
        DateTime occurredOnUtc)
    {
        var completedEvent = new AppointmentCompletedEvent
        {
            AppointmentId = appointment.AppointmentId,
            PatientId = appointment.PatientId,
            Notes = notes,
            CorrelationId = correlationId,
            OccurredAtUtc = occurredOnUtc
        };

        return ToOutboxMessage(
            completedEvent,
            appointment,
            JsonSerializer.Serialize(completedEvent, SerializerOptions),
            correlationId,
            occurredOnUtc);
    }

    /// <summary>The row every event after the booking is written as; see <see cref="Booked"/> for why each field is what it is.</summary>
    private static OutboxMessage ToOutboxMessage(
        IntegrationEvent integrationEvent,
        AppointmentEntity appointment,
        string payload,
        string correlationId,
        DateTime occurredOnUtc) => new()
    {
        MessageId = integrationEvent.MessageId,
        Topic = Topic,
        EventKey = appointment.AppointmentId.ToString(),
        EventType = integrationEvent.EventType,
        EventVersion = integrationEvent.Version,
        CorrelationId = correlationId,
        Payload = payload,
        OccurredOnUtc = occurredOnUtc
    };
}
