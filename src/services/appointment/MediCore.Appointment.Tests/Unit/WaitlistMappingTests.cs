using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Services;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>SCRUM-37 [QA] unit test: waitlist position ordering.</summary>
public sealed class WaitlistMappingTests
{
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Day = new(2026, 9, 25);

    [Fact]
    public void The_lowest_waiting_position_is_first_in_line()
    {
        var waiting = Positions(3, 8, 12);

        Assert.Equal(1, WaitlistMapping.PlaceInLine(Waiting(3), waiting));
        Assert.Equal(2, WaitlistMapping.PlaceInLine(Waiting(8), waiting));
        Assert.Equal(3, WaitlistMapping.PlaceInLine(Waiting(12), waiting));
    }

    [Fact]
    public void Entries_that_have_left_the_line_leave_gaps_that_do_not_count()
    {
        // Positions 4–11 were offered, accepted, declined or withdrawn; only waiting ones are passed.
        Assert.Equal(2, WaitlistMapping.PlaceInLine(Waiting(12), Positions(3, 12)));
    }

    [Fact]
    public void Position_decides_the_order_not_the_order_the_rows_arrive_in()
    {
        var waiting = Positions(12, 3, 8);

        Assert.Equal(1, WaitlistMapping.PlaceInLine(Waiting(3), waiting));
        Assert.Equal(3, WaitlistMapping.PlaceInLine(Waiting(12), waiting));
    }

    [Fact]
    public void Position_decides_the_order_even_against_the_join_time()
    {
        // Positions are handed out under the queue lock, so they are the join order. A clock that
        // disagrees — a retried join, a skewed host — must not reorder the queue.
        var early = Waiting(9);
        early.JoinedAtUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(2, WaitlistMapping.PlaceInLine(early, Positions(4, 9)));
    }

    [Fact]
    public void Other_queues_are_ignored()
    {
        var waiting = new List<WaitingPosition>
        {
            new(Guid.NewGuid(), Day, 1),        // another doctor, same day
            new(DoctorId, Day.AddDays(1), 1),   // same doctor, another day
            new(DoctorId, Day, 5)
        };

        Assert.Equal(1, WaitlistMapping.PlaceInLine(Waiting(5), waiting));
    }

    [Fact]
    public void A_waiting_entry_missing_from_the_positions_is_still_placed_behind_those_ahead()
    {
        Assert.Equal(2, WaitlistMapping.PlaceInLine(Waiting(7), Positions(2)));
    }

    [Theory]
    [InlineData(WaitlistStatus.Offered)]
    [InlineData(WaitlistStatus.Accepted)]
    [InlineData(WaitlistStatus.Declined)]
    [InlineData(WaitlistStatus.Expired)]
    [InlineData(WaitlistStatus.Withdrawn)]
    public void Only_a_waiting_entry_has_a_place_in_line(string status)
    {
        var entry = Waiting(3);
        entry.Status = status;

        Assert.Null(WaitlistMapping.PlaceInLine(entry, Positions(1, 2, 3)));
    }

    [Fact]
    public void A_patient_sees_their_entry_without_patient_or_slot_ids()
    {
        var entry = Waiting(3);
        entry.PatientNumber = "PAT-000042";
        entry.OfferedSlotId = Guid.NewGuid();
        var staffView = WaitlistMapping.ToResponse(new WaitlistListing(entry, "Dr. Silva", "Cardiology", null, null), 1);

        var patientView = WaitlistMapping.ToPatientResponse(staffView);

        Assert.Equal(entry.WaitlistEntryId, patientView.WaitlistEntryId);
        Assert.Equal("Dr. Silva", patientView.DoctorName);
        Assert.Equal(1, patientView.PlaceInLine);
        Assert.DoesNotContain(
            typeof(Application.DTOs.PatientWaitlistEntryResponse).GetProperties(),
            property => property.Name is "PatientId" or "PatientNumber" or "PatientName" or "OfferedSlotId" or "Position");
    }

    private static WaitlistEntry Waiting(int position) => new()
    {
        DoctorId = DoctorId,
        SlotDate = Day,
        PatientId = Guid.NewGuid(),
        Position = position,
        Status = WaitlistStatus.Waiting
    };

    private static List<WaitingPosition> Positions(params int[] positions) =>
        positions.Select(position => new WaitingPosition(DoctorId, Day, position)).ToList();
}
