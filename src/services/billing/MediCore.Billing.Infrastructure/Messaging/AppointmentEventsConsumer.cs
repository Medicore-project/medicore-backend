using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediCore.Billing.Infrastructure.Messaging;

public sealed class AppointmentEventsConsumer : BackgroundService
{
    private const string EventTypeHeader = "event-type";
    private readonly IAppointmentKafkaConsumerFactory _consumerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppointmentConsumerOptions _options;
    private readonly ILogger<AppointmentEventsConsumer> _logger;

    public AppointmentEventsConsumer(
        IAppointmentKafkaConsumerFactory consumerFactory,
        IServiceScopeFactory scopeFactory,
        AppointmentConsumerOptions options,
        ILogger<AppointmentEventsConsumer> logger)
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
            _logger.LogError(exception, "Kafka failed to deliver an appointment event to Billing.");
            await DelayAsync(cancellationToken);
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

            // The database transaction has committed before the Kafka offset is committed.
            consumer.Commit(consumed);
            _logger.LogInformation(
                "Billing handled {EventType} at {Offset} with result {Result}.",
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
            _logger.LogError(exception, "Billing failed at {Offset}; offset was not committed.", consumed.TopicPartitionOffset);
            consumer.Seek(consumed.TopicPartitionOffset);
            await DelayAsync(cancellationToken);
        }
    }

    private async Task DelayAsync(CancellationToken cancellationToken)
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
        var property = document.RootElement.EnumerateObject().FirstOrDefault(candidate =>
            string.Equals(candidate.Name, "eventType", StringComparison.OrdinalIgnoreCase));
        return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
    }
}
