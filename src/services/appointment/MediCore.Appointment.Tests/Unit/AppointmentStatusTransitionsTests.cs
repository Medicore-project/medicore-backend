using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Tests.Unit;

public sealed class AppointmentStatusTransitionsTests
{
    private static readonly string[] Statuses =
    [
        AppointmentStatus.Booked,
        AppointmentStatus.Cancelled,
        AppointmentStatus.Completed,
        AppointmentStatus.NoShow
    ];

    /// <summary>The whole table, written out: every pair that is allowed. Anything else is refused.</summary>
    private static readonly (string From, string To)[] AllowedPairs =
    [
        (AppointmentStatus.Booked, AppointmentStatus.Booked),
        (AppointmentStatus.Booked, AppointmentStatus.Cancelled),
        (AppointmentStatus.Booked, AppointmentStatus.Completed)
    ];

    public static TheoryData<string, string> EveryPair()
    {
        var data = new TheoryData<string, string>();
        foreach (var from in Statuses)
        {
            foreach (var to in Statuses)
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Only_a_booked_appointment_can_change_and_only_to_booked_cancelled_or_completed(
        string from, string to)
    {
        Assert.Equal(AllowedPairs.Contains((from, to)), AppointmentStatusTransitions.CanTransition(from, to));
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public void Cancelled_completed_and_no_show_are_terminal(string status)
    {
        Assert.True(AppointmentStatusTransitions.IsTerminal(status));
    }

    [Fact]
    public void Booked_is_not_terminal()
    {
        Assert.False(AppointmentStatusTransitions.IsTerminal(AppointmentStatus.Booked));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("booked")]
    [InlineData("")]
    public void An_unknown_status_allows_nothing_and_counts_as_terminal(string status)
    {
        // Case-sensitive, as the column is: "booked" is not a status this service ever writes.
        Assert.False(AppointmentStatusTransitions.CanTransition(status, AppointmentStatus.Cancelled));
        Assert.False(AppointmentStatusTransitions.CanTransition(AppointmentStatus.Booked, status));
        Assert.True(AppointmentStatusTransitions.IsTerminal(status));
    }
}
