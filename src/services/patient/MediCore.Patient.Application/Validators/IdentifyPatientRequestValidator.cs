using FluentValidation;
using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Validators;

/// <summary>Validates an <see cref="IdentifyPatientRequest"/>.</summary>
/// <remarks>
/// Shape only. Whether the pair actually matches a patient is not a validation concern — and must
/// not be reported as one, since a field-level "no such patient number" would tell a guesser which
/// numbers exist.
/// </remarks>
public sealed class IdentifyPatientRequestValidator : AbstractValidator<IdentifyPatientRequest>
{
    public IdentifyPatientRequestValidator()
    {
        RuleFor(r => r.PatientNumber)
            .NotEmpty()
            .MaximumLength(20);

        RuleFor(r => r.DateOfBirth)
            .NotEqual(default(DateOnly))
            .WithMessage("A date of birth is required.");
    }
}
