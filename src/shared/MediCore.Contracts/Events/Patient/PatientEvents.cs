namespace MediCore.Contracts.Events.Patient;

public sealed record PatientRegisteredEvent : IntegrationEvent
{
    public override string EventType => "patient.registered";

    public required Guid PatientId { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
}

public sealed record PatientUpdatedEvent : IntegrationEvent
{
    public override string EventType => "patient.updated";

    public required Guid PatientId { get; init; }
    public required string FullName { get; init; }
}
