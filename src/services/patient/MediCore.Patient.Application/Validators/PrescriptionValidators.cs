using FluentValidation;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Entities;

namespace MediCore.Patient.Application.Validators;

/// <summary>
/// Validates a <see cref="CreatePrescriptionRequest"/>.
/// Field length limits mirror the column constraints in <see cref="PrescriptionConfiguration"/>.
/// </summary>
public sealed class CreatePrescriptionRequestValidator : AbstractValidator<CreatePrescriptionRequest>
{
    public CreatePrescriptionRequestValidator()
    {
        RuleFor(r => r.Drug)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Dosage)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.Frequency)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.DurationDays)
            .GreaterThan(0)
            .LessThanOrEqualTo(3650)
            .WithMessage("Duration must be between 1 and 3,650 days (10 years).");

        RuleFor(r => r.Notes)
            .MaximumLength(2000)
            .When(r => r.Notes is not null);

        // MedicalRecordId is optional — no rule needed beyond nullability.
    }
}

/// <summary>
/// Validates an <see cref="UpdatePrescriptionRequest"/>.
/// Only active prescriptions can be updated; the service layer enforces that guard.
/// </summary>
public sealed class UpdatePrescriptionRequestValidator : AbstractValidator<UpdatePrescriptionRequest>
{
    public UpdatePrescriptionRequestValidator()
    {
        RuleFor(r => r.Drug)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(r => r.Dosage)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.Frequency)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(r => r.DurationDays)
            .GreaterThan(0)
            .LessThanOrEqualTo(3650)
            .WithMessage("Duration must be between 1 and 3,650 days (10 years).");

        RuleFor(r => r.Notes)
            .MaximumLength(2000)
            .When(r => r.Notes is not null);
    }
}
