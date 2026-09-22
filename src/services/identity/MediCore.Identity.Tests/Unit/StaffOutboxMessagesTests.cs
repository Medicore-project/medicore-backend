using System.Text.Json;
using MediCore.Contracts.Events.Staff;
using MediCore.Identity.Application.Entities;
using MediCore.Identity.Application.Messaging;

namespace MediCore.Identity.Tests.Unit;

public sealed class StaffOutboxMessagesTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 8, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Updated_targets_staff_events_keyed_by_staff_id()
    {
        var staff = Doctor();

        var message = StaffOutboxMessages.Updated(staff, Now);

        Assert.Equal("staff-events", message.Topic);
        Assert.Equal(staff.Id.ToString(), message.EventKey);
        Assert.Equal("staff.updated", message.EventType);
        Assert.Equal(Now, message.OccurredOnUtc);
    }

    [Fact]
    public void Updated_payload_carries_full_state_including_role_and_active_flag()
    {
        var staff = Doctor();
        staff.IsActive = false;

        var evt = Deserialize(StaffOutboxMessages.Updated(staff, Now));

        Assert.Equal(staff.Id, evt.StaffId);
        Assert.Equal("Tathira Samarakoon", evt.FullName);
        Assert.Equal("Neurology", evt.Specialization);
        Assert.Equal(staff.DepartmentId, evt.DepartmentId);
        Assert.Equal("Doctor", evt.Role);
        Assert.False(evt.IsActive);
        Assert.Equal(Now, evt.OccurredAtUtc);
        Assert.Equal(1, evt.Version);
    }

    [Fact]
    public void Updated_sends_null_role_when_user_is_not_loaded()
    {
        var staff = Doctor();
        staff.User = null;

        var evt = Deserialize(StaffOutboxMessages.Updated(staff, Now));

        Assert.Null(evt.Role);
    }

    [Fact]
    public void Each_updated_message_gets_its_own_message_id()
    {
        var staff = Doctor();

        var first = Deserialize(StaffOutboxMessages.Updated(staff, Now));
        var second = Deserialize(StaffOutboxMessages.Updated(staff, Now));

        Assert.NotEqual(first.MessageId, second.MessageId);
    }

    [Fact]
    public void Payload_published_before_role_and_active_existed_still_deserializes()
    {
        const string legacy = """
            {"StaffId":"6f1c0f5e-5a52-4a4f-9b5e-1f3c2d4e5a6b","FullName":"A B","Specialization":"",
             "DepartmentId":"00000000-0000-0000-0000-000000000001","MessageId":"8a1f0f5e-5a52-4a4f-9b5e-1f3c2d4e5a6b",
             "CorrelationId":"","OccurredAtUtc":"2026-09-01T00:00:00Z","EventType":"staff.updated","Version":1}
            """;

        var evt = JsonSerializer.Deserialize<StaffUpdatedEvent>(legacy)!;

        Assert.Null(evt.Role);
        Assert.Null(evt.IsActive);
    }

    private static StaffUpdatedEvent Deserialize(OutboxMessage message) =>
        JsonSerializer.Deserialize<StaffUpdatedEvent>(message.Payload)!;

    private static StaffProfile Doctor() => new()
    {
        FirstName = "Tathira",
        LastName = "Samarakoon",
        Specialization = "Neurology",
        DepartmentId = Guid.NewGuid(),
        IsActive = true,
        User = new User { Role = "Doctor" }
    };
}
