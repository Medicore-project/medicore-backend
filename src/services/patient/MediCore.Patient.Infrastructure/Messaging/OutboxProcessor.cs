using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Application.Entities;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediCore.Patient.Infrastructure.Messaging;

/// <summary>
/// Drains the outbox to Kafka, one small batch every few seconds.
/// </summary>
/// <remarks>
/// This is the half of the transactional outbox that can fail: an appointment completion commits its event row in
/// the same transaction as the appointment, and if the broker is unreachable the row simply stays
/// unprocessed and is retried on the next pass.
/// <para>
/// After MaxRetryAttempts are exceeded, the message is sent to the DLT topic (<topic>.dlt) to prevent
/// infinite retries.
/// </para>
/// </remarks>
public sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor> _logger;
    private const int MaxRetryAttempts = 5;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Patient outbox processor started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Patient outbox processor batch failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<IKafkaEventPublisher>();
        var messages = await repository.GetUnprocessedBatchAsync(20, cancellationToken);

        foreach (var message in messages)
        {
            // If max retry attempts exceeded, send to DLT instead of retrying
            if (message.RetryCount >= MaxRetryAttempts)
            {
                await SendToDeadLetterTopicAsync(message, publisher, cancellationToken);
                message.ProcessedOnUtc = DateTime.UtcNow; // Mark as processed to prevent infinite retries
                message.Error = "Max retry attempts exceeded, sent to DLT";
                _logger.LogWarning(
                    "Patient outbox message {OutboxMessageId} exceeded max retry attempts. Sent to DLT.",
                    message.Id);
                continue;
            }

            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                message.ProcessedOnUtc = DateTime.UtcNow;
                message.Error = null;
            }
            catch (Exception exception)
            {
                message.RetryCount++;
                message.Error = exception.Message.Length > 2_000
                    ? exception.Message[..2_000]
                    : exception.Message;

                _logger.LogError(
                    exception,
                    "Failed to publish Patient outbox message {OutboxMessageId}.",
                    message.Id);
            }
        }

        // One save for the whole batch: the messages that published are stamped and the ones that
        // failed carry their retry count, in a single round trip.
        if (messages.Count > 0)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SendToDeadLetterTopicAsync(
        OutboxMessage message,
        IKafkaEventPublisher publisher,
        CancellationToken cancellationToken)
    {
        var dltTopic = $"{message.Topic}.dlt";

        // Create headers similar to KafkaEventPublisher but add DLT-specific info
        var headers = new Headers
        {
            new Header("message-id", Encoding.UTF8.GetBytes(message.Id.ToString())),
            new Header("correlation-id", Encoding.UTF8.GetBytes(message.CorrelationId)),
            new Header("occurred-at-utc", Encoding.UTF8.GetBytes(message.OccurredOnUtc.ToString("O"))),
            new Header("event-type", Encoding.UTF8.GetBytes(message.EventType)),
            new Header("version", Encoding.UTF8.GetBytes(message.EventVersion.ToString())),
            new Header("original-topic", Encoding.UTF8.GetBytes(message.Topic)),
            new Header("retry-count", Encoding.UTF8.GetBytes(message.RetryCount.ToString())),
            new Header("dlT-reason", Encoding.UTF8.GetBytes("Max retry attempts exceeded"))
        };

        var kafkaMessage = new Message<string, string>
        {
            Key = message.EventKey,
            Value = message.Payload,
            Headers = headers
        };

        await publisher.ProduceAsync(dltTopic, kafkaMessage, cancellationToken);
    }
}