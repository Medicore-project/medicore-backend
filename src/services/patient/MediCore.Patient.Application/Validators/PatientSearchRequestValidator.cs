using FluentValidation;
using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Validators;

public sealed class PatientSearchRequestValidator : AbstractValidator<PatientSearchRequest>
{
    public PatientSearchRequestValidator()
    {
        RuleFor(request => request.Q)
            .MaximumLength(100)
            .When(request => request.Q is not null);

        RuleFor(request => request.Page)
            .InclusiveBetween(1, 1_000_000);

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, 100);
    }
}
