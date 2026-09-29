using MediCore.Appointment.Application.Scheduling;
using Microsoft.Extensions.Configuration;

namespace MediCore.Appointment.Tests.Unit;

public sealed class WaitlistOptionsTests
{
    [Fact]
    public void Without_configuration_an_offer_is_held_for_an_hour()
    {
        var options = new WaitlistOptions();

        Assert.Equal(TimeSpan.FromMinutes(60), options.EffectiveOfferWindow);
        Assert.Equal(TimeSpan.FromMinutes(15), options.EffectiveMinimumLead);
        Assert.Equal(30, options.SweepIntervalSeconds);
        Assert.Equal(3, options.EffectiveMaxActiveEntriesPerPatient);
    }

    [Fact]
    public void Every_setting_binds_from_the_documented_keys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Appointments:Waitlist:OfferWindowMinutes"] = "2",
                ["Appointments:Waitlist:MinimumLeadMinutes"] = "0",
                ["Appointments:Waitlist:SweepIntervalSeconds"] = "5",
                ["Appointments:Waitlist:MaxActiveEntriesPerPatient"] = "1"
            })
            .Build();

        var options = configuration.GetSection(WaitlistOptions.SectionName).Get<WaitlistOptions>()!;

        Assert.Equal(TimeSpan.FromMinutes(2), options.EffectiveOfferWindow);
        Assert.Equal(TimeSpan.Zero, options.EffectiveMinimumLead);
        Assert.Equal(5, options.SweepIntervalSeconds);
        Assert.Equal(1, options.EffectiveMaxActiveEntriesPerPatient);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void An_offer_window_of_zero_or_less_still_holds_the_slot_for_a_minute(int minutes)
    {
        // Otherwise an offer would expire the instant it was made and the queue would be skipped.
        var options = new WaitlistOptions { OfferWindowMinutes = minutes };

        Assert.Equal(TimeSpan.FromMinutes(1), options.EffectiveOfferWindow);
    }

    [Fact]
    public void A_negative_lead_is_read_as_none()
    {
        Assert.Equal(TimeSpan.Zero, new WaitlistOptions { MinimumLeadMinutes = -5 }.EffectiveMinimumLead);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_limit_of_zero_or_less_still_allows_one_queue(int limit)
    {
        Assert.Equal(1, new WaitlistOptions { MaxActiveEntriesPerPatient = limit }.EffectiveMaxActiveEntriesPerPatient);
    }
}
