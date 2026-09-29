using MediCore.Appointment.Api.Controllers;
using Microsoft.Extensions.Configuration;

namespace MediCore.Appointment.Tests.Unit;

public sealed class AppointmentRateLimitOptionsTests
{
    [Fact]
    public void Without_configuration_the_limit_is_what_it_always_was()
    {
        // Configurable for the load test only; production must not change by accident.
        var options = new AppointmentRateLimitOptions();

        Assert.Equal(100, options.PermitLimit);
        Assert.Equal(60, options.WindowSeconds);
    }

    [Fact]
    public void The_load_test_override_binds_from_the_documented_keys()
    {
        // The same key docker-compose.loadtest.yml sets, with ':' where the env var has '__'.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:AppointmentDefault:PermitLimit"] = "100000"
            })
            .Build();

        var options = configuration
            .GetSection(AppointmentRateLimitOptions.SectionName)
            .Get<AppointmentRateLimitOptions>()!;

        Assert.Equal(100_000, options.PermitLimit);
        Assert.Equal(60, options.WindowSeconds);
    }
}
