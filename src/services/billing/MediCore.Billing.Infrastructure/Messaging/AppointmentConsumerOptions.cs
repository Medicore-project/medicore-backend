namespace MediCore.Billing.Infrastructure.Messaging;

public sealed class AppointmentConsumerOptions
{
    public const string DefaultTopic = "appointment-events";
    public const string DefaultGroupId = "medicore-billing";

    public required string BootstrapServers { get; init; }
    public string? SaslUsername { get; init; }
    public string? SaslPassword { get; init; }
    public string Topic { get; init; } = DefaultTopic;
    public string GroupId { get; init; } = DefaultGroupId;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);
}
