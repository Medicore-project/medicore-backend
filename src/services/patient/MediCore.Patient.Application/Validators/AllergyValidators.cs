using FluentValidation;
using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Validators;

/// <summary>
/// Allowed values for <see cref="CreateAllergyRequest.Severity"/> and
/// <see cref="UpdateAllergyRequest.Severity"/>.
/// </summary>
file static class AllowedSeverities
{
    internal static readonly string[] Values = ["Mild", "Moderate", "Severe", "Unknown"];
}

/// <summary>Validates a <see cref="CreateAllergyRequest"/>.</summary>
public sealed class CreateAllergyRequestValidator : AbstractValidator<CreateAllergyRequest>
{
    public CreateAllergyRequestValidator()
    {
        RuleFor(r => r.Allergen)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Severity)
            .NotEmpty()
            .Must(s => AllowedSeverities.Values.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Severity must be one of: {string.Join(", ", AllowedSeverities.Values)}.");

        RuleFor(r => r.Reaction)
            .MaximumLength(500)
            .When(r => r.Reaction is not null);

        RuleFor(r => r.Notes)
            .MaximumLength(2000)
            .When(r => r.Notes is not null);
    }
}

/// <summary>Validates an <see cref="UpdateAllergyRequest"/>.</summary>
public sealed class UpdateAllergyRequestValidator : AbstractValidator<UpdateAllergyRequest>
{
    public UpdateAllergyRequestValidator()
    {
        RuleFor(r => r.Allergen)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Severity)
            .NotEmpty()
            .Must(s => AllowedSeverities.Values.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Severity must be one of: {string.Join(", ", AllowedSeverities.Values)}.");

        RuleFor(r => r.Reaction)
            .MaximumLength(500)
            .When(r => r.Reaction is not null);

        RuleFor(r => r.Notes)
            .MaximumLength(2000)
            .When(r => r.Notes is not null);
    }
}
