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
    private const string RetryCountHeader = "retry-count";
    private readonly IStaffKafkaConsumerFactory _consumerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StaffConsumerOptions _options;
    private readonly ILogger<StaffEventsConsumer> _logger;
    private readonly IProducer<string, string> _producer;

    public StaffEventsConsumer(
        IStaffKafkaConsumerFactory consumerFactory,
        IServiceScopeFactory scopeFactory,
        StaffConsumerOptions options,
        IProducer<string, string> producer,
        ILogger<StaffEventsConsumer> logger)
    {
        _consumerFactory = consumerFactory;
        _scopeFactory = scopeFactory;
        _options = options;
        _producer = producer;
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
            await HandleConsumeFailureAsync(consumed, 0, exception, cancellationToken);
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
            var retryCount = GetRetryCount(consumed.Message?.Headers);
            await HandleConsumeFailureAsync(consumed, retryCount, exception, cancellationToken);
        }
    }

    private async Task HandleConsumeFailureAsync(
        ConsumeResult<string, string> consumed,
        int currentRetryCount,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var headers = consumed.Message?.Headers;
        var nextRetryCount = currentRetryCount + 1;

        if (nextRetryCount > _options.MaxRetryAttempts)
        {
            // Send to DLT topic
            await SendToDeadLetterTopicAsync(consumed, headers, exception, cancellationToken);
            _logger.LogError(
                exception,
                "Staff event at {TopicPartitionOffset} exceeded max retry attempts ({MaxAttempts}). Sent to DLT.",
                consumed.TopicPartitionOffset,
                _options.MaxRetryAttempts);

            // Commit offset to prevent infinite redelivery of permanently failed message
            consumer.Commit(consumed);
        }
        else
        {
            // Send to retry topic with updated retry count
            await SendToRetryTopicAsync(consumed, headers, nextRetryCount, cancellationToken);
            var delay = CalculateRetryDelay(nextRetryCount);
            _logger.LogWarning(
                exception,
                "Staff event at {TopicPartitionOffset} failed (attempt {Attempt}/{MaxAttempts}). Retrying in {Delay}s.",
                consumed.TopicPartitionOffset,
                nextRetryCount,
                _options.MaxRetryAttempts,
                delay.TotalSeconds);

            // Apply delay before continuing (don't commit offset - will retry same message)
            await Task.Delay(delay, cancellationToken);
        }
    }

    private async Task SendToRetryTopicAsync(
        ConsumeResult<string, string> consumed,
        Headers? headers,
        int retryCount,
        CancellationToken cancellationToken)
    {
        var retryHeaders = new Headers();
        if (headers != null)
        {
            // Copy existing headers
            foreach (var header in headers)
            {
                retryHeaders.Add(header);
            }
        }

        // Add or update retry count header
        var retryCountBytes = BitConverter.GetBytes(retryCount);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(retryCountBytes);
        }
        retryHeaders.Add(RetryCountHeader, retryCountBytes);

        var retryMessage = new Message<string, string>
        {
            Key = consumed.Message?.Key,
            Value = consumed.Message?.Value,
            Headers = retryHeaders
        };

        var retryTopic = $"{_options.Topic}.retry";
        await _producer.ProduceAsync(retryTopic, retryMessage, cancellationToken);
    }

    private async Task SendToDeadLetterTopicAsync(
        ConsumeResult<string, string> consumed,
        Headers? headers,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var dltHeaders = new Headers();
        if (headers != null)
        {
            // Copy existing headers
            foreach (var header in headers)
            {
                dltHeaders.Add(header);
            }
        }

        // Add exception message header (truncated if too long)
        var exceptionMessage = exception.ToString();
        if (exceptionMessage.Length > 1000)
        {
            exceptionMessage = exceptionMessage.Substring(0, 1000) + "...[truncated]";
        }
        dltHeaders.Add("exception-message", Encoding.UTF8.GetBytes(exceptionMessage));
        dltHeaders.Add("original-topic", Encoding.UTF8.GetBytes(_options.Topic));

        var dltMessage = new Message<string, string>
        {
            Key = consumed.Message?.Key,
            Value = consumed.Message?.Value,
            Headers = dltHeaders
        };

        var dltTopic = $"{_options.Topic}.dlt";
        await _producer.ProduceAsync(dltTopic, dltMessage, cancellationToken);
    }

    private TimeSpan CalculateRetryDelay(int retryCount)
    {
        if (!_options.EnableExponentialBackoff)
        {
            return _options.RetryDelay;
        }

        // Exponential backoff: baseDelay * (2 ^ (retryCount - 1))
        // retryCount 1 -> baseDelay * 1
        // retryCount 2 -> baseDelay * 2
        // retryCount 3 -> baseDelay * 4
        // etc.
        double multiplier = Math.Pow(2, retryCount - 1);
        double delaySeconds = _options.BaseRetryDelay.TotalSeconds * multiplier;
        return TimeSpan.FromSeconds(delaySeconds);
    }

    private int GetRetryCount(Headers? headers)
    {
        if (headers == null)
        {
            return 0;
        }

        var retryCountHeader = headers.LastOrDefault(header =>
            header.IsNotNull && header.Name.Equals(RetryCountHeader, StringComparison.OrdinalIgnoreCase));

        if (retryCountHeader.HasValue)
        {
            try
            {
                var bytes = retryCountHeader.Value.GetValueBytes();
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }
                return BitConverter.ToInt32(bytes, 0);
            }
            catch
            {
                // If we can't parse the retry count, treat as 0 retries
                return 0;
            }
        }

        return 0;
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