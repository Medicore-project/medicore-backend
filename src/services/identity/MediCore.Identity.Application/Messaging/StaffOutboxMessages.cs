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
    public const string DoctorRole = "Doctor";

    /// <summary>
    /// One staff.updated per doctor, re-announcing current state so a consumer that joined after
    /// the original events aged out of Kafka's retention can rebuild its copy. Inactive doctors are
    /// included on purpose — the consumer needs to hear that they are not bookable. Everyone else
    /// is skipped.
    /// </summary>
    public static IReadOnlyList<OutboxMessage> RepublishDoctors(
        IEnumerable<StaffProfile> staff, DateTime occurredOnUtc) =>
        staff
            .Where(s => !s.IsDeleted && s.User?.Role == DoctorRole)
            .Select(s => Updated(s, occurredOnUtc))
            .ToList();

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
