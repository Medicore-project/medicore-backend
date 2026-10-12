using System.Globalization;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediCore.Billing.Infrastructure.Messaging;

public sealed class NotificationEventsConsumer : BackgroundService
{
    private const string EventTypeHeader = "event-type";
    private readonly INotificationKafkaConsumerFactory _consumerFactory;
    private readonly INotificationFailurePublisher _failurePublisher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotificationConsumerOptions _options;
    private readonly ILogger<NotificationEventsConsumer> _logger;
    private readonly bool _retryOnly;

    public NotificationEventsConsumer(
        INotificationKafkaConsumerFactory consumerFactory,
        INotificationFailurePublisher failurePublisher,
        IServiceScopeFactory scopeFactory,
        NotificationConsumerOptions options,
        ILogger<NotificationEventsConsumer> logger,
        bool retryOnly = false)
    {
        _consumerFactory = consumerFactory;
        _failurePublisher = failurePublisher;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _retryOnly = retryOnly;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var consumer = _consumerFactory.Create();
        consumer.Subscribe(_retryOnly
            ? _options.Topics.Select(topic => topic + ".retry")
            : _options.Topics);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ConsumeOnceAsync(consumer, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown.
        }
        finally
        {
            consumer.Close();
        }
    }

    public async Task ConsumeOnceAsync(
        INotificationKafkaConsumerClient consumer,
        CancellationToken cancellationToken)
    {
        ConsumeResult<string, string> consumed;
        try
        {
            consumed = consumer.Consume(cancellationToken);
        }
        catch (ConsumeException exception)
        {
            _logger.LogError(exception, "Kafka failed to deliver a notification event.");
            await DelayAsync(_options.RetryDelay, cancellationToken);
            return;
        }

        try
        {
            await DelayForRetryAsync(consumed, cancellationToken);
            var payload = consumed.Message?.Value ?? throw new JsonException("The Kafka message has no payload.");
            var eventType = ReadEventType(consumed.Message.Headers, payload);
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<INotificationEventProcessor>();
            var result = await processor.ProcessAsync(eventType, payload, cancellationToken);
            consumer.Commit(consumed);
            _logger.LogInformation(
                "Notification consumer handled {EventType} at {Offset} with result {Result}.",
                eventType ?? "unknown",
                consumed.TopicPartitionOffset,
                result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            try
            {
                var route = await _failurePublisher.PublishAsync(consumed, exception, cancellationToken);
                consumer.Commit(consumed);
                _logger.LogWarning(
                    exception,
                    "Notification delivery failed at {Offset}; routed attempt {Attempt} to {Topic}.",
                    consumed.TopicPartitionOffset,
                    route.Attempt,
                    route.Topic);
            }
            catch (Exception routingException) when (routingException is not OperationCanceledException)
            {
                _logger.LogError(
                    routingException,
                    "Notification failure could not be routed at {Offset}; offset remains uncommitted.",
                    consumed.TopicPartitionOffset);
                consumer.Seek(consumed.TopicPartitionOffset);
                await DelayAsync(_options.RetryDelay, cancellationToken);
            }
        }
    }

    private async Task DelayForRetryAsync(ConsumeResult<string, string> consumed, CancellationToken cancellationToken)
    {
        if (!consumed.Topic.EndsWith(".retry", StringComparison.Ordinal)) return;
        var attempt = int.TryParse(
            NotificationFailurePublisher.ReadHeader(consumed.Message.Headers, "retry-attempt"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? Math.Max(1, parsed)
            : 1;
        var multiplier = Math.Pow(2, Math.Min(attempt - 1, 6));
        await DelayAsync(TimeSpan.FromMilliseconds(_options.RetryDelay.TotalMilliseconds * multiplier), cancellationToken);
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay > TimeSpan.Zero ? Task.Delay(delay, cancellationToken) : Task.CompletedTask;

    private static string? ReadEventType(Headers? headers, string payload)
    {
        var header = headers?.LastOrDefault(candidate =>
            string.Equals(candidate.Key, EventTypeHeader, StringComparison.OrdinalIgnoreCase));
        if (header is not null)
        {
            var value = Encoding.UTF8.GetString(header.GetValueBytes());
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        using var document = JsonDocument.Parse(payload);
        var property = document.RootElement.EnumerateObject().FirstOrDefault(candidate =>
            string.Equals(candidate.Name, "eventType", StringComparison.OrdinalIgnoreCase));
        return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
    }
}
