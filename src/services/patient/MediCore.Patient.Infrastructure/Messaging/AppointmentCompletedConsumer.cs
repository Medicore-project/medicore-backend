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
    private readonly IAppointmentKafkaConsumerFactory _consumerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppointmentConsumerOptions _options;
    private readonly ILogger<AppointmentCompletedConsumer> _logger;

    public AppointmentCompletedConsumer(
        IAppointmentKafkaConsumerFactory consumerFactory,
        IServiceScopeFactory scopeFactory,
        AppointmentConsumerOptions options,
        ILogger<AppointmentCompletedConsumer> logger)
    {
        _consumerFactory = consumerFactory;
        _scopeFactory = scopeFactory;
        _options = options;
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
            await DelayBeforeRetryAsync(cancellationToken);
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
