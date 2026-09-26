namespace MediCore.Appointment.Application.Scheduling;

/// <summary>
/// The clinic's cancellation policy, bound from the <c>Appointments:Cancellation</c> configuration
/// section.
/// </summary>
/// <remarks>
/// SCRUM-36. The window applies to cancelling <em>and</em> rescheduling, for every caller: moving an
/// appointment at the last minute empties the original time just as surely as cancelling it. The
/// one exception is a stranded booking, whose slot a schedule change has flagged; the clinic caused
/// that, so it can always be moved.
/// </remarks>
public sealed class CancellationPolicyOptions
{
    /// <summary>Configuration section name in <c>appsettings.json</c>.</summary>
    public const string SectionName = "Appointments:Cancellation";

    /// <summary>
    /// How many hours before its start an appointment stops being cancellable or reschedulable.
    /// Zero turns the window off, leaving only "it has already started" refused. A negative value
    /// is treated as zero.
    /// </summary>
    public int WindowHours { get; set; } = 24;
}
