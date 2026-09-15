namespace MediCore.Patient.Infrastructure.Messaging;

public sealed class AppointmentConsumerOptions
{
    public const string DefaultTopic = "appointment-events";
    public const string DefaultGroupId = "medicore-patient";

    public required string BootstrapServers { get; init; }
    public string Topic { get; init; } = DefaultTopic;
    public string GroupId { get; init; } = DefaultGroupId;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);
}
