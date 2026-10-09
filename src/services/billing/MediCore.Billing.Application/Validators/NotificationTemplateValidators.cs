using FluentValidation;
using MediCore.Billing.Application.DTOs;

namespace MediCore.Billing.Application.Validators;

public sealed class CreateNotificationTemplateRequestValidator : AbstractValidator<CreateNotificationTemplateRequest>
{
    public CreateNotificationTemplateRequestValidator()
    {
        RuleFor(request => request.Code)
            .NotEmpty()
            .MaximumLength(80)
            .Matches("^[A-Za-z0-9]+(?:[._-][A-Za-z0-9]+)*$");
        RuleFor(request => request.Name).NotEmpty().MaximumLength(150);
        RuleFor(request => request.SubjectTemplate).NotEmpty().MaximumLength(300);
        RuleFor(request => request.BodyTemplate).NotEmpty().MaximumLength(10_000);
    }
}

public sealed class UpdateNotificationTemplateRequestValidator : AbstractValidator<UpdateNotificationTemplateRequest>
{
    public UpdateNotificationTemplateRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(150);
        RuleFor(request => request.SubjectTemplate).NotEmpty().MaximumLength(300);
        RuleFor(request => request.BodyTemplate).NotEmpty().MaximumLength(10_000);
    }
}
