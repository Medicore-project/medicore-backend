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

    [Fact]
    public void Republish_queues_only_doctors()
    {
        var doctor = Doctor();
        var nurse = Doctor();
        nurse.User = new User { Role = "Nurse" };
        var admin = Doctor();
        admin.User = new User { Role = "Admin" };

        var messages = StaffOutboxMessages.RepublishDoctors([doctor, nurse, admin], Now);

        var only = Assert.Single(messages);
        Assert.Equal(doctor.Id.ToString(), only.EventKey);
        Assert.Equal("staff.updated", only.EventType);
    }

    [Fact]
    public void Republish_includes_inactive_doctors_so_consumers_learn_they_are_not_bookable()
    {
        var inactive = Doctor();
        inactive.IsActive = false;

        var messages = StaffOutboxMessages.RepublishDoctors([inactive], Now);

        Assert.False(Deserialize(Assert.Single(messages)).IsActive);
    }

    [Fact]
    public void Republish_skips_deleted_staff_and_staff_without_a_loaded_user()
    {
        var deleted = Doctor();
        deleted.IsDeleted = true;
        var noUser = Doctor();
        noUser.User = null;

        var messages = StaffOutboxMessages.RepublishDoctors([deleted, noUser], Now);

        Assert.Empty(messages);
    }

    [Fact]
    public void Republish_matches_the_role_name_exactly()
    {
        var lowercase = Doctor();
        lowercase.User = new User { Role = "doctor" };

        Assert.Empty(StaffOutboxMessages.RepublishDoctors([lowercase], Now));
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
