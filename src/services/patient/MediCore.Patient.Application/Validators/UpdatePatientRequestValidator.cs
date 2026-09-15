using System.Text.RegularExpressions;
using FluentValidation;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Services;

namespace MediCore.Patient.Application.Validators;

public sealed partial class UpdatePatientRequestValidator : AbstractValidator<UpdatePatientRequest>
{
    private static readonly string[] AllowedGenders = ["Male", "Female", "Other", "PreferNotToSay", "Prefer not to say"];

    public UpdatePatientRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");

        RuleFor(x => x.DateOfBirth)
            .NotEmpty().WithMessage("Date of birth is required.")
            .LessThanOrEqualTo(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime))
            .WithMessage("Date of birth cannot be in the future.");

        RuleFor(x => x.Gender)
            .NotEmpty().WithMessage("Gender is required.")
            .Must(gender => AllowedGenders.Contains(gender?.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Gender must be Male, Female, Other, or Prefer not to say.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone number is required.")
            .Must(BeAValidPhone).WithMessage("Phone number must use a valid Sri Lankan format, such as 0771234567 or +94771234567.");

        RuleFor(x => x.AddressLine1)
            .NotEmpty().WithMessage("Address line 1 is required.")
            .MaximumLength(200).WithMessage("Address line 1 must not exceed 200 characters.");

        RuleFor(x => x.AddressLine2)
            .MaximumLength(200).WithMessage("Address line 2 must not exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.AddressLine2));

        RuleFor(x => x.District)
            .NotEmpty().WithMessage("District is required.")
            .MaximumLength(100).WithMessage("District must not exceed 100 characters.");

        RuleFor(x => x.EmergencyContactName)
            .MaximumLength(200).WithMessage("Emergency contact name must not exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.EmergencyContactName));

        RuleFor(x => x.EmergencyContactPhone)
            .Must(BeAValidPhone).WithMessage("Emergency contact phone must use a valid Sri Lankan format.")
            .When(x => !string.IsNullOrWhiteSpace(x.EmergencyContactPhone));
    }

    private static bool BeAValidPhone(string? phone) =>
        !string.IsNullOrWhiteSpace(phone) && PhoneRegex().IsMatch(PatientInputNormalizer.Phone(phone));

    [GeneratedRegex(@"^(?:\+94|0)\d{9}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();
}
