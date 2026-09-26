using MediCore.Appointment.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediCore.Appointment.Infrastructure.Messaging;

/// <summary>
/// Drains the outbox to Kafka, one small batch every few seconds.
/// </summary>
/// <remarks>
/// This is the half of the transactional outbox that can fail: a booking commits its event row in
/// the same transaction as the appointment, and if the broker is unreachable the row simply stays
/// unprocessed and is retried on the next pass (SCRUM-34 AC5).
/// <para>
/// Otherwise identical to the Patient and Identity processors, including the hard-coded interval
/// and batch size and the absence of a retry cap or backoff. A permanently unpublishable message is
/// therefore retried forever; that is a gap shared by all three services and is recorded rather than
/// fixed here, so the three stay comparable.
/// </para>
/// <para>
/// The one deliberate difference (SCRUM-36): once a message fails, every later message in the
/// batch with the <strong>same event key</strong> is held back until the next pass. Every event of
/// one appointment shares its key, and the key is what keeps them on one partition in order — but
/// only if they reach the broker in order. Without this, a booking that failed to publish would be
/// overtaken by its own cancellation in the same batch, and billing would be asked to void an
/// invoice it has not raised. Messages for other appointments are not held back. The Patient and
/// Identity processors are left unchanged by this ticket; whether they need the same guard is
/// recorded as an open item in the SCRUM-36 write-up.
/// </para>
/// </remarks>
public sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Appointment outbox processor started.");

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
                _logger.LogError(exception, "Appointment outbox processor batch failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    /// <summary>
    /// Publishes one batch and records the outcome. Public so tests can drive a single pass
    /// without the background loop, as <c>StaffEventsConsumer.ConsumeOnceAsync</c> is.
    /// </summary>
    public async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<IKafkaEventPublisher>();
        var messages = await repository.GetUnprocessedBatchAsync(20, cancellationToken);

        // Keys whose earlier message failed in this pass. A later message for the same key waits:
        // it is neither published nor counted as a retry, and the next pass (which reads oldest
        // first) meets the failed one ahead of it again.
        var blockedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            if (blockedKeys.Contains(message.EventKey))
            {
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
                blockedKeys.Add(message.EventKey);
                message.RetryCount++;
                message.Error = exception.Message.Length > 2_000
                    ? exception.Message[..2_000]
                    : exception.Message;

                _logger.LogError(
                    exception,
                    "Failed to publish Appointment outbox message {OutboxMessageId}.",
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
}
