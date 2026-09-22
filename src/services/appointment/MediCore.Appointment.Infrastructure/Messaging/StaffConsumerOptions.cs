namespace MediCore.Appointment.Infrastructure.Messaging;

public sealed class StaffConsumerOptions
{
    public const string DefaultTopic = "staff-events";
    public const string DefaultGroupId = "medicore-appointment";

    public required string BootstrapServers { get; init; }
    public string Topic { get; init; } = DefaultTopic;
    public string GroupId { get; init; } = DefaultGroupId;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);
}
