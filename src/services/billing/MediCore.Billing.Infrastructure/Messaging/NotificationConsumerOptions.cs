namespace MediCore.Billing.Infrastructure.Messaging;

public sealed class NotificationConsumerOptions
{
    public static readonly string[] DefaultTopics = ["patient-events", "appointment-events", "billing-events"];

    public required string BootstrapServers { get; init; }
    public string? SaslUsername { get; init; }
    public string? SaslPassword { get; init; }
    public string GroupId { get; init; } = "medicore-billing-notifications";
    public IReadOnlyList<string> Topics { get; init; } = DefaultTopics;
    public int MaxDeliveryAttempts { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);
}
