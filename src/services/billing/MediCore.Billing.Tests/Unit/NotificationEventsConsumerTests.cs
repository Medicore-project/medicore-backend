using System.Text;
using Confluent.Kafka;
using MediCore.Billing.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Billing.Tests.Unit;

public sealed class NotificationEventsConsumerTests
{
    [Fact]
    public async Task Successful_delivery_commits_without_routing_failure()
    {
        var processor = new FakeProcessor();
        var failurePublisher = new FakeFailurePublisher();
        var kafka = new FakeConsumer();

        await CreateConsumer(processor, failurePublisher).ConsumeOnceAsync(kafka, CancellationToken.None);

        Assert.True(kafka.Committed);
        Assert.False(kafka.Sought);
        Assert.False(failurePublisher.Called);
    }

    [Fact]
    public async Task Failed_delivery_commits_only_after_retry_publish_succeeds()
    {
        var processor = new FakeProcessor { Exception = new InvalidOperationException("SMTP unavailable") };
        var failurePublisher = new FakeFailurePublisher();
        var kafka = new FakeConsumer();

        await CreateConsumer(processor, failurePublisher).ConsumeOnceAsync(kafka, CancellationToken.None);

        Assert.True(failurePublisher.Called);
        Assert.True(kafka.Committed);
        Assert.False(kafka.Sought);
    }

    [Fact]
    public async Task Failed_retry_publish_leaves_offset_uncommitted()
    {
        var processor = new FakeProcessor { Exception = new InvalidOperationException("SMTP unavailable") };
        var failurePublisher = new FakeFailurePublisher { Exception = new InvalidOperationException("Kafka unavailable") };
        var kafka = new FakeConsumer();

        await CreateConsumer(processor, failurePublisher).ConsumeOnceAsync(kafka, CancellationToken.None);

        Assert.True(failurePublisher.Called);
        Assert.False(kafka.Committed);
        Assert.True(kafka.Sought);
    }

    private static NotificationEventsConsumer CreateConsumer(
        FakeProcessor processor,
        FakeFailurePublisher failurePublisher)
    {
        var provider = new ServiceCollection()
            .AddSingleton<INotificationEventProcessor>(processor)
            .BuildServiceProvider();
        return new NotificationEventsConsumer(
            null!,
            failurePublisher,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new NotificationConsumerOptions
            {
                BootstrapServers = "unused",
                RetryDelay = TimeSpan.Zero
            },
            NullLogger<NotificationEventsConsumer>.Instance);
    }

    private sealed class FakeProcessor : INotificationEventProcessor
    {
        public Exception? Exception { get; set; }
        public Task<NotificationEventProcessingResult> ProcessAsync(string? eventType, string payload, CancellationToken cancellationToken)
        {
            if (Exception is not null) throw Exception;
            return Task.FromResult(NotificationEventProcessingResult.Sent);
        }
    }

    private sealed class FakeFailurePublisher : INotificationFailurePublisher
    {
        public bool Called { get; private set; }
        public Exception? Exception { get; set; }
        public Task<NotificationFailureRoute> PublishAsync(ConsumeResult<string, string> consumed, Exception exception, CancellationToken cancellationToken)
        {
            Called = true;
            if (Exception is not null) throw Exception;
            return Task.FromResult(new NotificationFailureRoute("patient-events.retry", 1, false));
        }
    }

    private sealed class FakeConsumer : INotificationKafkaConsumerClient
    {
        public bool Committed { get; private set; }
        public bool Sought { get; private set; }

        public void Subscribe(IEnumerable<string> topics) { }
        public ConsumeResult<string, string> Consume(CancellationToken cancellationToken) => new()
        {
            Topic = "patient-events",
            Partition = new Partition(0),
            Offset = new Offset(1),
            Message = new Message<string, string>
            {
                Key = "patient-1",
                Value = "{}",
                Headers = new Headers { { "event-type", Encoding.UTF8.GetBytes("patient.registered") } }
            }
        };
        public void Commit(ConsumeResult<string, string> result) => Committed = true;
        public void Seek(TopicPartitionOffset offset) => Sought = true;
        public void Close() { }
        public void Dispose() { }
    }
}
