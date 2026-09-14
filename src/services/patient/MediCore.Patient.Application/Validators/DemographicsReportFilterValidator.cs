using FluentValidation;
using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Validators;

public sealed class DemographicsReportFilterValidator : AbstractValidator<DemographicsReportFilter>
{
    public DemographicsReportFilterValidator()
    {
        RuleFor(filter => filter.AgeBand)
            .Must(ageBand => DemographicsReportOptions.TryGetAgeRange(ageBand, out _))
            .When(filter => !string.IsNullOrWhiteSpace(filter.AgeBand))
            .WithMessage($"Age band must be one of: {string.Join(", ", DemographicsReportOptions.AgeBands)}.");

        RuleFor(filter => filter.Gender)
            .Must(gender => DemographicsReportOptions.Genders.Contains(
                DemographicsReportOptions.NormalizeGender(gender!),
                StringComparer.Ordinal))
            .When(filter => !string.IsNullOrWhiteSpace(filter.Gender))
            .WithMessage($"Gender must be one of: {string.Join(", ", DemographicsReportOptions.Genders)}.");

        RuleFor(filter => filter.District)
            .MaximumLength(100)
            .When(filter => filter.District is not null);

        RuleFor(filter => filter.To)
            .Must(to => to != DateOnly.MaxValue)
            .When(filter => filter.To.HasValue)
            .WithMessage("To date must be earlier than 9999-12-31.");

        RuleFor(filter => filter)
            .Must(filter => !filter.From.HasValue || !filter.To.HasValue || filter.From <= filter.To)
            .WithName(nameof(DemographicsReportFilter.To))
            .WithMessage("To date must be on or after from date.");
    }
}
