using FluentValidation;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Validators;

public sealed class RevenueReportFilterValidator : AbstractValidator<RevenueReportFilter>
{
    public const int MaxRangeDays = 366;

    public RevenueReportFilterValidator()
    {
        var earliest = new DateOnly(1900, 1, 1);
        var latest = new DateOnly(2100, 12, 31);
        RuleFor(x => x.DepartmentId).NotEqual(Guid.Empty).When(x => x.DepartmentId.HasValue);
        RuleFor(x => x.PaymentMethod)
            .Must(value => value is null || PaymentMethods.All.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Payment method must be Cash, Card or Insurance.");
        RuleFor(x => x.Currency)
            .Matches("^[A-Za-z]{3}$")
            .When(x => x.Currency is not null);
        RuleFor(x => x.DepartmentName).MaximumLength(100);
        RuleFor(x => x.From).Must(value => value!.Value >= earliest && value.Value <= latest)
            .When(x => x.From.HasValue)
            .WithMessage("From date must be between 1900-01-01 and 2100-12-31.");
        RuleFor(x => x.To).Must(value => value!.Value >= earliest && value.Value <= latest)
            .When(x => x.To.HasValue)
            .WithMessage("To date must be between 1900-01-01 and 2100-12-31.");
        RuleFor(x => x.To).Must(value => value != DateOnly.MaxValue)
            .When(x => x.To.HasValue);
        RuleFor(x => x).Must(x => x.From <= x.To)
            .When(x => x.From.HasValue && x.To.HasValue)
            .OverridePropertyName(nameof(RevenueReportFilter.To))
            .WithMessage("To date must be on or after from date.");
        RuleFor(x => x).Must(x => x.To!.Value.DayNumber - x.From!.Value.DayNumber + 1 <= MaxRangeDays)
            .When(x => x.From.HasValue && x.To.HasValue && x.From <= x.To)
            .OverridePropertyName(nameof(RevenueReportFilter.To))
            .WithMessage($"Period cannot exceed {MaxRangeDays} days.");
    }
}
