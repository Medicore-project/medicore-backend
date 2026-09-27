using MediCore.Appointment.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediCore.Appointment.Infrastructure.Waitlist;

/// <summary>
/// Runs the waitlist sweep every <c>Appointments:Waitlist:SweepIntervalSeconds</c> (SCRUM-37): the
/// loop that makes an unanswered offer expire and pass on (AC3) without anyone asking.
/// </summary>
/// <remarks>
/// The same shape as <c>OutboxProcessor</c>: a fresh scope per pass, and any failure logged and
/// retried on the next pass rather than stopping the loop. Registered whether or not Kafka is
/// configured — the waitlist needs only the database — and not at all when the interval is zero or
/// less, which is how the integration test host keeps it out of its tests.
/// </remarks>
public sealed class WaitlistSweepProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;
    private readonly ILogger<WaitlistSweepProcessor> _logger;

    public WaitlistSweepProcessor(
        IServiceScopeFactory scopeFactory,
        WaitlistSweepSchedule schedule,
        ILogger<WaitlistSweepProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _interval = schedule.Interval;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Waitlist sweeper started; every {Interval}.", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Waitlist sweep failed.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    /// <summary>One pass, in its own scope. Public so tests can drive it without the loop.</summary>
    public async Task<WaitlistSweepSummary> SweepOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sweeper = scope.ServiceProvider.GetRequiredService<IWaitlistSweeper>();
        return await sweeper.SweepAsync(cancellationToken);
    }
}

/// <summary>How often the sweeper runs, resolved once from configuration at startup.</summary>
public sealed record WaitlistSweepSchedule(TimeSpan Interval);
