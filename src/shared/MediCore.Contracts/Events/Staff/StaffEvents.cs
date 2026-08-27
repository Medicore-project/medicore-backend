namespace MediCore.Contracts.Events.Staff;

public sealed record StaffUpdatedEvent : IntegrationEvent
{
    public override string EventType => "staff.updated";

    public required Guid StaffId { get; init; }
    public required string FullName { get; init; }
    public required string Specialization { get; init; }
    public required Guid DepartmentId { get; init; }
}

public sealed record StaffDeactivatedEvent : IntegrationEvent
{
    public override string EventType => "staff.deactivated";

    public required Guid StaffId { get; init; }
}
