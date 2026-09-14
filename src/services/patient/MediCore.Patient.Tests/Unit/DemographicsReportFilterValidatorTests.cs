using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Validators;

namespace MediCore.Patient.Tests.Unit;

public sealed class DemographicsReportFilterValidatorTests
{
    private readonly DemographicsReportFilterValidator _validator = new();

    [Fact]
    public void No_filters_is_valid()
    {
        var result = _validator.Validate(new DemographicsReportFilter());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(DemographicsReportOptions.Age0To17, 0, 17)]
    [InlineData(DemographicsReportOptions.Age18To34, 18, 34)]
    [InlineData(DemographicsReportOptions.Age35To49, 35, 49)]
    [InlineData(DemographicsReportOptions.Age50To64, 50, 64)]
    [InlineData(DemographicsReportOptions.Age65Plus, 65, null)]
    public void Supported_age_bands_map_to_inclusive_boundaries(
        string ageBand,
        int expectedMinimum,
        int? expectedMaximum)
    {
        var valid = DemographicsReportOptions.TryGetAgeRange(ageBand, out var range);

        Assert.True(valid);
        Assert.Equal(expectedMinimum, range.MinimumAge);
        Assert.Equal(expectedMaximum, range.MaximumAge);
        Assert.True(_validator.Validate(new DemographicsReportFilter(ageBand, null, null, null, null)).IsValid);
    }

    [Theory]
    [InlineData("Under 18")]
    [InlineData("18-35")]
    [InlineData("65")]
    public void Unknown_age_band_is_rejected(string ageBand)
    {
        var result = _validator.Validate(new DemographicsReportFilter(ageBand, null, null, null, null));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(DemographicsReportFilter.AgeBand));
    }

    [Theory]
    [InlineData("Male")]
    [InlineData("female")]
    [InlineData("Other")]
    [InlineData("Prefer not to say")]
    [InlineData("PreferNotToSay")]
    public void Supported_gender_is_accepted_case_insensitively(string gender)
    {
        var result = _validator.Validate(new DemographicsReportFilter(null, gender, null, null, null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Unknown_gender_is_rejected()
    {
        var result = _validator.Validate(new DemographicsReportFilter(null, "Unknown", null, null, null));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(DemographicsReportFilter.Gender));
    }

    [Fact]
    public void District_longer_than_database_limit_is_rejected()
    {
        var result = _validator.Validate(new DemographicsReportFilter(
            null,
            null,
            new string('a', 101),
            null,
            null));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(DemographicsReportFilter.District));
    }

    [Fact]
    public void Reversed_date_range_is_rejected()
    {
        var result = _validator.Validate(new DemographicsReportFilter(
            null,
            null,
            null,
            new DateOnly(2026, 9, 15),
            new DateOnly(2026, 9, 14)));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(DemographicsReportFilter.To));
    }

    [Fact]
    public void Maximum_date_is_rejected_because_end_boundary_must_advance_one_day()
    {
        var result = _validator.Validate(new DemographicsReportFilter(null, null, null, null, DateOnly.MaxValue));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(DemographicsReportFilter.To));
    }
}
