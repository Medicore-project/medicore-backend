using FluentValidation;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Validators;

/// <summary>Validates a <see cref="CreatePublicHolidayRequest"/>.</summary>
public sealed class CreatePublicHolidayRequestValidator
    : AbstractValidator<CreatePublicHolidayRequest>
{
    public CreatePublicHolidayRequestValidator()
    {
        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(200);
    }
}

/// <summary>Validates a <see cref="CreateDoctorLeaveRequest"/>.</summary>
public sealed class CreateDoctorLeaveRequestValidator
    : AbstractValidator<CreateDoctorLeaveRequest>
{
    public CreateDoctorLeaveRequestValidator()
    {
        RuleFor(r => r.DoctorId).NotEmpty();

        RuleFor(r => r.EndDate)
            .GreaterThanOrEqualTo(r => r.StartDate)
            .WithMessage("End date must not be before the start date.");

        RuleFor(r => r.Reason)
            .MaximumLength(500)
            .When(r => r.Reason is not null);
    }
}

/// <summary>Validates a <see cref="ReviewDoctorLeaveRequest"/>.</summary>
public sealed class ReviewDoctorLeaveRequestValidator
    : AbstractValidator<ReviewDoctorLeaveRequest>
{
    /// <summary>
    /// A decision is only ever a grant or a refusal. <see cref="LeaveStatus.Pending"/> is
    /// deliberately not accepted here: a request cannot be moved back to undecided.
    /// </summary>
    private static readonly string[] AllowedDecisions = [LeaveStatus.Approved, LeaveStatus.Rejected];

    public ReviewDoctorLeaveRequestValidator()
    {
        RuleFor(r => r.Decision)
            .NotEmpty()
            .Must(d => AllowedDecisions.Contains(d, StringComparer.Ordinal))
            .WithMessage($"Decision must be one of: {string.Join(", ", AllowedDecisions)}.");

        RuleFor(r => r.Notes)
            .MaximumLength(500)
            .When(r => r.Notes is not null);
    }
}
