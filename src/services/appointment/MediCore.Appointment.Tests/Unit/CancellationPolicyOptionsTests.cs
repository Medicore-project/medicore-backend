using MediCore.Appointment.Application.Scheduling;
using Microsoft.Extensions.Configuration;

namespace MediCore.Appointment.Tests.Unit;

public sealed class CancellationPolicyOptionsTests
{
    [Fact]
    public void Without_configuration_the_window_is_24_hours()
    {
        Assert.Equal(24, new CancellationPolicyOptions().WindowHours);
    }

    [Fact]
    public void The_window_binds_from_the_documented_key()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Appointments:Cancellation:WindowHours"] = "48"
            })
            .Build();

        var options = configuration
            .GetSection(CancellationPolicyOptions.SectionName)
            .Get<CancellationPolicyOptions>()!;

        Assert.Equal(48, options.WindowHours);
    }
}
