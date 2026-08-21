using MediCore.Identity.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediCore.Identity.Infrastructure.Messaging;

public sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox processor started.");

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
                _logger.LogError(exception, "Outbox processor batch failed.");
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
            try
            {
                await publisher.PublishAsync(
                    message.Topic,
                    message.Id.ToString(),
                    message.EventType,
                    message.Payload,
                    cancellationToken);

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
                    "Failed to publish outbox message {OutboxMessageId}.",
                    message.Id);
            }
        }

        if (messages.Count > 0)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
    }
}