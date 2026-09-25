using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace MediCore.Appointment.Tests.Integration;

/// <summary>
/// SCRUM-35: the service-wide rate limit really is read from configuration by the host — not
/// just bindable in isolation — so the load test's override takes effect.
/// </summary>
/// <remarks>Needs the local Postgres, like every test that starts the host.</remarks>
[Trait("Category", "Integration")]
public sealed class RateLimitConfigurationTests : IClassFixture<AppointmentApiFactory>
{
    private readonly AppointmentApiFactory _factory;

    public RateLimitConfigurationTests(AppointmentApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task The_configured_permit_limit_is_the_one_the_host_enforces()
    {
        // A host of its own, so its limiter window is not shared with any other test.
        await using var limited = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:AppointmentDefault:PermitLimit", "2"));
        var client = limited.CreateClient();

        // Anonymous and cheap, and behind the service-wide policy like every controller.
        const string path = "/api/public/booking/specializations";

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync(path)).StatusCode);
    }
}
