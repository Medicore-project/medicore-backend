using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Validators;

namespace MediCore.Appointment.Tests.Unit;

public sealed class UtilisationReportFilterValidatorTests
{
    private readonly UtilisationReportFilterValidator _validator = new();

    [Fact]
    public void No_filters_at_all_is_valid()
    {
        Assert.True(_validator.Validate(new UtilisationReportFilter()).IsValid);
    }

    [Fact]
    public void Every_filter_together_is_valid()
    {
        var result = _validator.Validate(new UtilisationReportFilter
        {
            DoctorId = Guid.NewGuid(),
            DepartmentId = Guid.NewGuid(),
            DepartmentName = "Cardiology",
            From = new DateOnly(2026, 9, 1),
            To = new DateOnly(2026, 9, 30)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Empty_ids_are_refused()
    {
        var result = _validator.Validate(new UtilisationReportFilter
        {
            DoctorId = Guid.Empty,
            DepartmentId = Guid.Empty
        });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UtilisationReportFilter.DoctorId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UtilisationReportFilter.DepartmentId));
    }

    [Fact]
    public void A_backwards_range_is_refused_on_to()
    {
        var result = _validator.Validate(new UtilisationReportFilter
        {
            From = new DateOnly(2026, 9, 10),
            To = new DateOnly(2026, 9, 9)
        });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(UtilisationReportFilter.To), error.PropertyName);
        Assert.Equal("To date must be on or after from date.", error.ErrorMessage);
    }

    [Fact]
    public void A_single_day_is_a_valid_range()
    {
        var day = new DateOnly(2026, 9, 10);

        Assert.True(_validator.Validate(new UtilisationReportFilter { From = day, To = day }).IsValid);
    }

    [Theory]
    [InlineData(366, true)]
    [InlineData(367, false)]
    public void The_period_covers_at_most_366_days_inclusive(int days, bool valid)
    {
        var from = new DateOnly(2026, 1, 1);

        var result = _validator.Validate(new UtilisationReportFilter { From = from, To = from.AddDays(days - 1) });

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            Assert.Equal(nameof(UtilisationReportFilter.To), Assert.Single(result.Errors).PropertyName);
        }
    }

    [Fact]
    public void The_last_representable_date_is_refused()
    {
        var result = _validator.Validate(new UtilisationReportFilter { To = DateOnly.MaxValue });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UtilisationReportFilter.To));
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void A_department_name_is_at_most_100_characters(int length, bool valid)
    {
        var result = _validator.Validate(new UtilisationReportFilter
        {
            DepartmentId = Guid.NewGuid(),
            DepartmentName = new string('x', length)
        });

        Assert.Equal(valid, result.IsValid);
    }
}
