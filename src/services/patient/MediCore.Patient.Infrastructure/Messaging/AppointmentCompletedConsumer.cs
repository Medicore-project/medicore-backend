using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediCore.Patient.Infrastructure.Messaging;

public sealed class AppointmentCompletedConsumer : BackgroundService
{
    private const string EventTypeHeader = "event-type";
    private const string RetryCountHeader = "retry-count";
    private readonly IAppointmentKafkaConsumerFactory _consumerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppointmentConsumerOptions _options;
    private readonly ILogger<AppointmentCompletedConsumer> _logger;
    private readonly IProducer<string, string> _producer;

    public AppointmentCompletedConsumer(
        IAppointmentKafkaConsumerFactory consumerFactory,
        IServiceScopeFactory scopeFactory,
        AppointmentConsumerOptions options,
        IProducer<string, string> producer,
        ILogger<AppointmentCompletedConsumer> logger)
    {
        _consumerFactory = consumerFactory;
        _scopeFactory = scopeFactory;
        _options = options;
        _producer = producer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var consumer = _consumerFactory.Create();
        consumer.Subscribe(_options.Topic);

        _logger.LogInformation(
            "Appointment event consumer started for topic {Topic} in group {ConsumerGroup}.",
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
            _logger.LogInformation("Appointment event consumer stopped.");
        }
    }

    public async Task ConsumeOnceAsync(
        IAppointmentKafkaConsumerClient consumer,
        CancellationToken cancellationToken)
    {
        ConsumeResult<string, string> consumed;
        try
        {
            consumed = consumer.Consume(cancellationToken);
        }
        catch (ConsumeException exception)
        {
            _logger.LogError(exception, "Kafka failed to deliver an appointment event.");
            await HandleConsumeFailureAsync(consumed, 0, exception, cancellationToken);
            return;
        }

        try
        {
            var payload = consumed.Message?.Value
                ?? throw new JsonException("The Kafka message has no payload.");
            var eventType = ReadEventType(consumed.Message?.Headers, payload);

            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IAppointmentEventProcessor>();
            var result = await processor.ProcessAsync(eventType, payload, cancellationToken);

            // Manual commit is intentionally after all database/outbox work succeeds.
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
                "Appointment event at {TopicPartitionOffset} failed; offset was not committed.",
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
                "Appointment event at {TopicPartitionOffset} exceeded max retry attempts ({MaxAttempts}). Sent to DLT.",
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
                "Appointment event at {TopicPartitionOffset} failed (attempt {Attempt}/{MaxAttempts}). Retrying in {Delay}s.",
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
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "eventType", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }
}