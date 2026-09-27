using MediCore.Appointment.Application.Services;
using MediCore.Appointment.Infrastructure;
using MediCore.Appointment.Infrastructure.Waitlist;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>SCRUM-37: when the background sweep runs, and that one pass does one sweep.</summary>
public sealed class WaitlistSweepProcessorTests
{
    [Fact]
    public void Without_configuration_the_sweep_runs_every_30_seconds()
    {
        var services = Register(interval: null);

        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(WaitlistSweepProcessor));
        Assert.Equal(TimeSpan.FromSeconds(30), Schedule(services).Interval);
    }

    [Fact]
    public void The_interval_comes_from_configuration()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), Schedule(Register(interval: "5")).Interval);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    public void An_interval_of_zero_or_less_switches_the_sweep_off(string interval)
    {
        // How the integration test host keeps background writes out of its tests.
        var services = Register(interval);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(WaitlistSweepSchedule));
    }

    [Fact]
    public async Task One_pass_runs_one_sweep_in_its_own_scope()
    {
        var sweeper = new CountingSweeper();
        using var provider = new ServiceCollection()
            .AddScoped<IWaitlistSweeper>(_ => sweeper)
            .BuildServiceProvider();
        var processor = new WaitlistSweepProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new WaitlistSweepSchedule(TimeSpan.FromSeconds(30)),
            NullLogger<WaitlistSweepProcessor>.Instance);

        var summary = await processor.SweepOnceAsync(CancellationToken.None);

        Assert.Equal(1, sweeper.Sweeps);
        Assert.Equal(CountingSweeper.Summary, summary);
    }

    private static ServiceCollection Register(string? interval)
    {
        var settings = new Dictionary<string, string?>();
        if (interval is not null)
        {
            settings["Appointments:Waitlist:SweepIntervalSeconds"] = interval;
        }

        var services = new ServiceCollection();
        DependencyInjection.AddWaitlistSweeper(
            services, new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services;
    }

    private static WaitlistSweepSchedule Schedule(ServiceCollection services) =>
        (WaitlistSweepSchedule)Assert.Single(
            services, descriptor => descriptor.ServiceType == typeof(WaitlistSweepSchedule)).ImplementationInstance!;

    private sealed class CountingSweeper : IWaitlistSweeper
    {
        public static readonly WaitlistSweepSummary Summary = new(1, 0, 2, 0);

        public int Sweeps { get; private set; }

        public Task<WaitlistSweepSummary> SweepAsync(CancellationToken cancellationToken = default)
        {
            Sweeps++;
            return Task.FromResult(Summary);
        }
    }
}
