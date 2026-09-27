namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// The one place that says which <see cref="AppointmentStatus"/> changes are allowed.
/// </summary>
/// <remarks>
/// <para>
/// SCRUM-36. Only a booked appointment can change: it can be cancelled, completed, or moved to
/// another slot (a reschedule, which keeps it <see cref="AppointmentStatus.Booked"/>).
/// <see cref="AppointmentStatus.Cancelled"/>, <see cref="AppointmentStatus.Completed"/> and
/// <see cref="AppointmentStatus.NoShow"/> are terminal: each has already told billing or the
/// patient record something that a later change could not take back.
/// </para>
/// <para>
/// A table rather than a state-machine library: four statuses and three edges do not need one, and
/// a table is what the tests enumerate.
/// </para>
/// </remarks>
public static class AppointmentStatusTransitions
{
    private static readonly IReadOnlyDictionary<string, string[]> Allowed =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [AppointmentStatus.Booked] =
            [
                AppointmentStatus.Booked,
                AppointmentStatus.Cancelled,
                AppointmentStatus.Completed
            ],
            [AppointmentStatus.Cancelled] = [],
            [AppointmentStatus.Completed] = [],
            [AppointmentStatus.NoShow] = []
        };

    /// <summary>
    /// True when an appointment in <paramref name="from"/> may move to <paramref name="to"/>.
    /// <c>Booked → Booked</c> is a reschedule. An unknown status allows nothing.
    /// </summary>
    public static bool CanTransition(string from, string to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to, StringComparer.Ordinal);

    /// <summary>True when nothing can change an appointment in <paramref name="status"/> any more.</summary>
    public static bool IsTerminal(string status) =>
        !Allowed.TryGetValue(status, out var targets) || targets.Length == 0;
}
