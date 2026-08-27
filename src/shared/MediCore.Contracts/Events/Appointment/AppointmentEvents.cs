namespace MediCore.Contracts.Events.Appointment;

public sealed record AppointmentBookedEvent : IntegrationEvent
{
    public override string EventType => "appointment.booked";

    public required Guid AppointmentId { get; init; }
    public required Guid PatientId { get; init; }
    public required Guid DoctorId { get; init; }
    public required DateTime SlotStart { get; init; }
    public required string ServiceCode { get; init; }
}

public sealed record AppointmentCancelledEvent : IntegrationEvent
{
    public override string EventType => "appointment.cancelled";

    public required Guid AppointmentId { get; init; }
    public required string Reason { get; init; }
}

public sealed record AppointmentCompletedEvent : IntegrationEvent
{
    public override string EventType => "appointment.completed";

    public required Guid AppointmentId { get; init; }
    public required Guid PatientId { get; init; }
    public required string Notes { get; init; }
}
