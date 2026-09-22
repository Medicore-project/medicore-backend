using System.Text.Json;
using MediCore.Contracts.Events.Staff;
using MediCore.Identity.Application.Entities;

namespace MediCore.Identity.Application.Messaging;

/// <summary>
/// Builds outbox rows for staff-events. Every staff.updated carries the profile's full current
/// state — role and active flag included — so a consumer can upsert its copy from any one event
/// without having seen the ones before it.
/// </summary>
public static class StaffOutboxMessages
{
    public const string Topic = "staff-events";

    /// <param name="staff">Must have <see cref="StaffProfile.User"/> loaded, or the role is sent as null.</param>
    public static OutboxMessage Updated(StaffProfile staff, DateTime occurredOnUtc)
    {
        var updatedEvent = new StaffUpdatedEvent
        {
            StaffId = staff.Id,
            FullName = staff.FullName,
            Specialization = staff.Specialization,
            DepartmentId = staff.DepartmentId,
            Role = staff.User?.Role,
            IsActive = staff.IsActive,
            OccurredAtUtc = occurredOnUtc
        };

        return new OutboxMessage
        {
            Topic = Topic,
            EventKey = staff.Id.ToString(),
            EventType = updatedEvent.EventType,
            Payload = JsonSerializer.Serialize(updatedEvent),
            OccurredOnUtc = occurredOnUtc
        };
    }
}
