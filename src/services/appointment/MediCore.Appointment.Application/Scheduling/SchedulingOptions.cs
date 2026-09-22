namespace MediCore.Appointment.Application.Scheduling;

/// <summary>
/// Configuration for slot generation, bound from the <c>Scheduling</c> configuration section.
/// </summary>
public sealed class SchedulingOptions
{
    /// <summary>Configuration section name in <c>appsettings.json</c>.</summary>
    public const string SectionName = "Scheduling";

    /// <summary>
    /// How many days ahead of today slots are generated for when a schedule is created or changed.
    /// SCRUM-32 AC1 requires 60 days by default and that the horizon be configurable.
    /// </summary>
    public int SlotHorizonDays { get; set; } = 60;
}
