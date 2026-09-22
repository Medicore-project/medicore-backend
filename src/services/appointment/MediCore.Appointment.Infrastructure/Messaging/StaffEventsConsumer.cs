using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediCore.Appointment.Infrastructure.Messaging;

/// <summary>
/// Reads Identity's staff-events into the doctor cache. Kafka creates the consumer group
/// (<see cref="StaffConsumerOptions.DefaultGroupId"/>) the first time this subscribes.
/// </summary>
public sealed class StaffEventsConsumer : BackgroundService
{
    private const string EventTypeHeader = "event-type";
    private readonly IStaffKafkaConsumerFactory _consumerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StaffConsumerOptions _options;
    private readonly ILogger<StaffEventsConsumer> _logger;

    public StaffEventsConsumer(
        IStaffKafkaConsumerFactory consumerFactory,
        IServiceScopeFactory scopeFactory,
        StaffConsumerOptions options,
        ILogger<StaffEventsConsumer> logger)
    {
        _consumerFactory = consumerFactory;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Consume blocks its thread; yield first so host startup is not held up.
        await Task.Yield();
        using var consumer = _consumerFactory.Create();
        consumer.Subscribe(_options.Topic);

        _logger.LogInformation(
            "Staff event consumer started for topic {Topic} in group {ConsumerGroup}.",
            _options.Topic,
            _options.GroupId);

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
            _logger.LogInformation("Staff event consumer stopped.");
        }
    }

    public async Task ConsumeOnceAsync(
        IStaffKafkaConsumerClient consumer,
        CancellationToken cancellationToken)
    {
        ConsumeResult<string, string> consumed;
        try
        {
            consumed = consumer.Consume(cancellationToken);
        }
        catch (ConsumeException exception)
        {
            _logger.LogError(exception, "Kafka failed to deliver a staff event.");
            await DelayBeforeRetryAsync(cancellationToken);
            return;
        }

        try
        {
            var payload = consumed.Message?.Value;
            var eventType = ReadEventType(consumed.Message?.Headers, payload);

            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IStaffEventProcessor>();
            var result = await processor.ProcessAsync(eventType, payload, cancellationToken);

            // Every returned result is final — applied, or deliberately skipped — so commit. Only
            // after the database work, so a crash before this point redelivers the message.
            consumer.Commit(consumed);
            _logger.LogInformation(
                "Handled {EventType} at {TopicPartitionOffset} with result {ProcessingResult}; offset committed.",
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
            _logger.LogError(
                exception,
                "Staff event at {TopicPartitionOffset} failed; offset was not committed.",
                consumed.TopicPartitionOffset);

            // Rewind immediately. Merely skipping Commit would let a later offset commit past this failure.
            consumer.Seek(consumed.TopicPartitionOffset);
            await DelayBeforeRetryAsync(cancellationToken);
        }
    }

    private async Task DelayBeforeRetryAsync(CancellationToken cancellationToken)
    {
        if (_options.RetryDelay > TimeSpan.Zero)
        {
            await Task.Delay(_options.RetryDelay, cancellationToken);
        }
    }

    private static string? ReadEventType(Headers? headers, string? payload)
    {
        var header = headers?.LastOrDefault(candidate =>
            string.Equals(candidate.Key, EventTypeHeader, StringComparison.OrdinalIgnoreCase));
        if (header is not null)
        {
            var value = Encoding.UTF8.GetString(header.GetValueBytes());
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        if (string.IsNullOrWhiteSpace(payload)) return null;

        // Unlike the patient consumer, an unreadable payload is not thrown: it would fail the same
        // way on every retry and wedge the partition. The processor rejects the missing event type
        // instead, and the offset is committed.
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "eventType", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
