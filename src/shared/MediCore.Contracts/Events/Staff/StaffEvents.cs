namespace MediCore.Contracts.Events.Staff;

public sealed record StaffCreatedEvent : IntegrationEvent
{
    public override string EventType => "staff.created";

    public required Guid StaffId { get; init; }
    public required Guid UserId { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public required string Specialization { get; init; }
    public required Guid DepartmentId { get; init; }
}

public sealed record StaffUpdatedEvent : IntegrationEvent
{
    public override string EventType => "staff.updated";

    public required Guid StaffId { get; init; }
    public required string FullName { get; init; }
    public required string Specialization { get; init; }
    public required Guid DepartmentId { get; init; }

    // Added after v1 shipped, so optional: payloads published before these existed still
    // deserialize, and consumers must treat null as "not stated" rather than a real value.
    public string? Role { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record StaffDeactivatedEvent : IntegrationEvent
{
    public override string EventType => "staff.deactivated";

    public required Guid StaffId { get; init; }
}
