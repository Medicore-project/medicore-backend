using FluentValidation;
using MediCore.Identity.Application.DTOs;

namespace MediCore.Identity.Application.Validators;

public class CreateStaffRequestValidator : AbstractValidator<CreateStaffRequest>
{
    private static readonly string[] AllowedRoles = { "Admin", "Doctor", "Nurse", "Receptionist", "Patient" };

    public CreateStaffRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long.")
            .MaximumLength(100).WithMessage("Password must not exceed 100 characters.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .Must(role => AllowedRoles.Contains(role))
            .WithMessage($"Role must be one of: {string.Join(", ", AllowedRoles)}");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");

        RuleFor(x => x.Phone)
            .MaximumLength(50).WithMessage("Phone must not exceed 50 characters.")
            .When(x => !string.IsNullOrEmpty(x.Phone));

        RuleFor(x => x.Specialization)
            .MaximumLength(100).WithMessage("Specialization must not exceed 100 characters.")
            .When(x => !string.IsNullOrEmpty(x.Specialization));

        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department ID is required.");
    }
}

public class UpdateStaffRequestValidator : AbstractValidator<UpdateStaffRequest>
{
    public UpdateStaffRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");

        RuleFor(x => x.Phone)
            .MaximumLength(50).WithMessage("Phone must not exceed 50 characters.")
            .When(x => !string.IsNullOrEmpty(x.Phone));

        RuleFor(x => x.Specialization)
            .MaximumLength(100).WithMessage("Specialization must not exceed 100 characters.")
            .When(x => !string.IsNullOrEmpty(x.Specialization));

        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department ID is required.");
    }
}
