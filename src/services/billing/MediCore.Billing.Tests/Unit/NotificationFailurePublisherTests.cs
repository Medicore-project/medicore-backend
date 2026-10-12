using System.Text;
using Confluent.Kafka;
using MediCore.Billing.Infrastructure.Messaging;

namespace MediCore.Billing.Tests.Unit;

public sealed class NotificationFailurePublisherTests
{
    private readonly NotificationFailurePublisher _publisher = new(
        null!,
        new NotificationConsumerOptions
        {
            BootstrapServers = "unused",
            MaxDeliveryAttempts = 3
        });

    [Fact]
    public void First_failure_routes_to_retry_topic()
    {
        var route = _publisher.DetermineRoute(Consumed("patient-events"));

        Assert.Equal("patient-events.retry", route.Topic);
        Assert.Equal(1, route.Attempt);
        Assert.False(route.IsDeadLetter);
    }

    [Fact]
    public void Third_failure_routes_to_original_topics_dlt()
    {
        var headers = new Headers
        {
            { "original-topic", Encoding.UTF8.GetBytes("billing-events") },
            { "retry-attempt", Encoding.UTF8.GetBytes("2") }
        };

        var route = _publisher.DetermineRoute(Consumed("billing-events.retry", headers));

        Assert.Equal("billing-events.dlt", route.Topic);
        Assert.Equal(3, route.Attempt);
        Assert.True(route.IsDeadLetter);
    }

    private static ConsumeResult<string, string> Consumed(string topic, Headers? headers = null) => new()
    {
        Topic = topic,
        Partition = new Partition(0),
        Offset = new Offset(1),
        Message = new Message<string, string>
        {
            Key = "key",
            Value = "{}",
            Headers = headers ?? []
        }
    };
}
