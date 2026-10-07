using FluentValidation;
using MediCore.Billing.Application.DTOs;

namespace MediCore.Billing.Application.Validators;

public sealed class CreateServiceTariffRequestValidator : AbstractValidator<CreateServiceTariffRequest>
{
    public CreateServiceTariffRequestValidator()
    {
        RuleFor(request => request.ServiceCode)
            .NotEmpty()
            .MaximumLength(50)
            .Matches("^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$")
            .WithMessage("Service code may contain letters, numbers and single hyphens only.");
        AddSharedRules();
    }

    private void AddSharedRules()
    {
        RuleFor(request => request.Description).NotEmpty().MaximumLength(300);
        RuleFor(request => request.UnitPrice).GreaterThan(0).LessThanOrEqualTo(999_999_999m);
        RuleFor(request => request.Currency)
            .NotEmpty()
            .Length(3)
            .Matches("^[A-Za-z]{3}$")
            .WithMessage("Currency must be a three-letter code.");
        RuleFor(request => request.EffectiveFromUtc)
            .NotEmpty()
            .GreaterThan(DateTime.UnixEpoch);
    }
}

public sealed class UpdateServiceTariffRequestValidator : AbstractValidator<UpdateServiceTariffRequest>
{
    public UpdateServiceTariffRequestValidator()
    {
        RuleFor(request => request.Description).NotEmpty().MaximumLength(300);
        RuleFor(request => request.UnitPrice).GreaterThan(0).LessThanOrEqualTo(999_999_999m);
        RuleFor(request => request.Currency)
            .NotEmpty()
            .Length(3)
            .Matches("^[A-Za-z]{3}$")
            .WithMessage("Currency must be a three-letter code.");
        RuleFor(request => request.EffectiveFromUtc)
            .NotEmpty()
            .GreaterThan(DateTime.UnixEpoch);
    }
}
