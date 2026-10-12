using FluentValidation;
using MediCore.Billing.Application.DTOs;

namespace MediCore.Billing.Application.Validators;

public sealed class OutstandingReportFilterValidator : AbstractValidator<OutstandingReportFilter>
{
    public OutstandingReportFilterValidator()
    {
        var earliest = new DateOnly(1900, 1, 1);
        var latest = new DateOnly(2100, 12, 31);
        RuleFor(x => x.DepartmentId).NotEqual(Guid.Empty).When(x => x.DepartmentId.HasValue);
        RuleFor(x => x.Currency).Matches("^[A-Za-z]{3}$").When(x => x.Currency is not null);
        RuleFor(x => x.DepartmentName).MaximumLength(100);
        RuleFor(x => x.From).Must(date => date!.Value >= earliest && date.Value <= latest)
            .When(x => x.From.HasValue);
        RuleFor(x => x.To).Must(date => date!.Value >= earliest && date.Value <= latest)
            .When(x => x.To.HasValue);
        RuleFor(x => x).Must(x => x.From <= x.To)
            .When(x => x.From.HasValue && x.To.HasValue)
            .OverridePropertyName(nameof(OutstandingReportFilter.To))
            .WithMessage("To date must be on or after from date.");
        RuleFor(x => x).Must(x => x.To!.Value.DayNumber - x.From!.Value.DayNumber + 1 <= 366)
            .When(x => x.From.HasValue && x.To.HasValue && x.From <= x.To)
            .OverridePropertyName(nameof(OutstandingReportFilter.To))
            .WithMessage("Period cannot exceed 366 days.");
    }
}
