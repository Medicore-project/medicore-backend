using System.Text.Json;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Messaging;
using MediCore.Contracts.Events.Appointment;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Tests.Unit;

public sealed class AppointmentOutboxMessagesTests
{
    private static readonly Guid AppointmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DoctorId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SlotId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTime StartUtc = new(2026, 9, 24, 3, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime OccurredOnUtc = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);

    private static readonly JsonSerializerOptions ReaderOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_booking_is_announced_on_the_appointment_events_topic()
    {
        var message = AppointmentOutboxMessages.Booked(Appointment(), "corr-1", OccurredOnUtc);

        Assert.Equal("appointment-events", message.Topic);
        Assert.Equal("appointment-events", AppointmentOutboxMessages.Topic);
        Assert.Equal("appointment.booked", message.EventType);
        Assert.Equal(OccurredOnUtc, message.OccurredOnUtc);
        Assert.Null(message.ProcessedOnUtc);
        Assert.Equal(0, message.RetryCount);
    }

    [Fact]
    public void The_partition_key_is_the_appointment_so_its_later_events_stay_in_order()
    {
        // Not the slot and not the patient: the aggregate is the appointment, so a later
        // appointment.cancelled must land on the same partition as this booking.
        var message = AppointmentOutboxMessages.Booked(Appointment(), "corr-1", OccurredOnUtc);

        Assert.Equal(AppointmentId.ToString(), message.EventKey);
        Assert.NotEqual(SlotId.ToString(), message.EventKey);
        Assert.NotEqual(PatientId.ToString(), message.EventKey);
    }

    [Fact]
    public void The_row_carries_the_events_own_message_id()
    {
        // The column is unique-indexed and has no database default, so a row built without this
        // would write an empty Guid and collide with the next one.
        var message = AppointmentOutboxMessages.Booked(Appointment(), "corr-1", OccurredOnUtc);
        var published = Deserialize(message);

        Assert.NotEqual(Guid.Empty, message.MessageId);
        Assert.Equal(published.MessageId, message.MessageId);
    }

    [Fact]
    public void Two_bookings_get_distinct_message_ids()
    {
        var first = AppointmentOutboxMessages.Booked(Appointment(), "corr-1", OccurredOnUtc);
        var second = AppointmentOutboxMessages.Booked(Appointment(), "corr-2", OccurredOnUtc);

        Assert.NotEqual(first.MessageId, second.MessageId);
    }

    [Fact]
    public void The_correlation_id_is_carried_through_non_null()
    {
        // The column is required, so a null here is a runtime failure at save time.
        var message = AppointmentOutboxMessages.Booked(Appointment(), "corr-abc", OccurredOnUtc);

        Assert.Equal("corr-abc", message.CorrelationId);
        Assert.Equal("corr-abc", Deserialize(message).CorrelationId);
    }

    [Fact]
    public void The_version_stays_at_one_because_the_patient_consumer_dead_letters_anything_else()
    {
        var message = AppointmentOutboxMessages.Booked(Appointment(), "corr-1", OccurredOnUtc);

        Assert.Equal(1, message.EventVersion);
        Assert.Equal(1, Deserialize(message).Version);
    }

    [Fact]
    public void The_payload_carries_the_service_code_for_billing()
    {
        // SCRUM-34 AC4.
        var appointment = Appointment();
        appointment.ServiceCode = ServiceCodes.SpecialistConsultation;

        var published = Deserialize(AppointmentOutboxMessages.Booked(appointment, "corr-1", OccurredOnUtc));

        Assert.Equal(ServiceCodes.SpecialistConsultation, published.ServiceCode);
        Assert.Equal(AppointmentId, published.AppointmentId);
        Assert.Equal(PatientId, published.PatientId);
        Assert.Equal(DoctorId, published.DoctorId);
        Assert.Equal(StartUtc, published.SlotStart);
    }

    [Fact]
    public void The_payload_is_camel_cased_like_every_other_producer_on_this_topic()
    {
        var message = AppointmentOutboxMessages.Booked(Appointment(), "corr-1", OccurredOnUtc);

        Assert.Contains("\"serviceCode\"", message.Payload);
        Assert.Contains("\"appointmentId\"", message.Payload);
        Assert.DoesNotContain("\"ServiceCode\"", message.Payload);
    }

    // ── SCRUM-36: appointment.cancelled ──────────────────────────────────────

    [Fact]
    public void A_cancellation_goes_to_the_same_topic_and_partition_as_its_booking()
    {
        var booked = AppointmentOutboxMessages.Booked(Appointment(), "corr-1", OccurredOnUtc);
        var cancelled = AppointmentOutboxMessages.Cancelled(Appointment(), "Travelling", "corr-2", OccurredOnUtc);

        Assert.Equal("appointment.cancelled", cancelled.EventType);
        Assert.Equal(booked.Topic, cancelled.Topic);
        Assert.Equal(booked.EventKey, cancelled.EventKey);
        Assert.Equal(AppointmentId.ToString(), cancelled.EventKey);
    }

    [Fact]
    public void A_cancellation_carries_its_own_message_id_the_reason_and_version_one()
    {
        var message = AppointmentOutboxMessages.Cancelled(Appointment(), "Travelling", "corr-2", OccurredOnUtc);
        var published = JsonSerializer.Deserialize<AppointmentCancelledEvent>(message.Payload, ReaderOptions)!;

        Assert.NotEqual(Guid.Empty, message.MessageId);
        Assert.Equal(published.MessageId, message.MessageId);
        Assert.Equal(AppointmentId, published.AppointmentId);
        Assert.Equal("Travelling", published.Reason);
        Assert.Equal("corr-2", published.CorrelationId);
        Assert.Equal("corr-2", message.CorrelationId);
        Assert.Equal(OccurredOnUtc, published.OccurredAtUtc);
        Assert.Equal(OccurredOnUtc, message.OccurredOnUtc);
        Assert.Equal(1, message.EventVersion);
        Assert.Contains("\"reason\"", message.Payload);
    }

    private static AppointmentBookedEvent Deserialize(OutboxMessage message) =>
        JsonSerializer.Deserialize<AppointmentBookedEvent>(message.Payload, ReaderOptions)!;

    private static AppointmentEntity Appointment() => new()
    {
        AppointmentId = AppointmentId,
        SlotId = SlotId,
        PatientId = PatientId,
        DoctorId = DoctorId,
        StartUtc = StartUtc,
        EndUtc = StartUtc.AddMinutes(30),
        SlotDate = new DateOnly(2026, 9, 24),
        DurationMinutes = 30,
        ServiceCode = ServiceCodes.GeneralConsultation,
        Status = AppointmentStatus.Booked
    };
}
