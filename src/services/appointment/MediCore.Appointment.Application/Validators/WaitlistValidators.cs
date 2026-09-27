using FluentValidation;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Validators;

/// <summary>Validates a <see cref="JoinWaitlistRequest"/>.</summary>
/// <remarks>
/// Only the body. Whether the day is full, in the past or past the horizon needs the slots and a
/// clock, so the waitlist service decides. <c>PatientId</c> is optional on the wire, as in booking:
/// a booking token supplies it from a claim, so the controller decides whether one was resolved.
/// </remarks>
public sealed class JoinWaitlistRequestValidator : AbstractValidator<JoinWaitlistRequest>
{
    public JoinWaitlistRequestValidator()
    {
        RuleFor(r => r.DoctorId).NotEmpty();

        RuleFor(r => r.Date)
            .NotEqual(default(DateOnly))
            .WithMessage("A date is required.");

        RuleFor(r => r.ServiceCode)
            .Must(ServiceCodes.IsKnown)
            .WithMessage($"Service code must be one of: {string.Join(", ", ServiceCodes.All)}.")
            .When(r => r.ServiceCode is not null);
    }
}

/// <summary>Validates a <see cref="RemoveWaitlistEntryRequest"/>.</summary>
public sealed class RemoveWaitlistEntryRequestValidator : AbstractValidator<RemoveWaitlistEntryRequest>
{
    /// <summary>The <c>waitlist_entries.ClosedReason</c> column's width.</summary>
    public const int MaxReasonLength = 500;

    public RemoveWaitlistEntryRequestValidator()
    {
        RuleFor(r => r.Reason)
            .Must(reason => reason!.Trim().Length <= MaxReasonLength)
            .WithMessage($"The reason must be at most {MaxReasonLength} characters.")
            .When(r => r.Reason is not null);
    }
}
