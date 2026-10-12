using FluentValidation;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Validators;

public sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(request => request.Amount)
            .GreaterThan(0m)
            .WithMessage("Payment amount must be greater than zero.");

        RuleFor(request => request.Method)
            .Must(method => PaymentMethods.Normalize(method) is not null)
            .WithMessage($"Payment method must be one of: {string.Join(", ", PaymentMethods.All)}.");
    }
}
