using FluentValidation;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Validators;

/// <summary>Validates a <see cref="BookAppointmentRequest"/>.</summary>
/// <remarks>
/// Only the rules that are a pure function of the body live here, as in every other validator in
/// this service. "Has this slot already passed?" is not one of them — the body carries a slot id,
/// not a time, so answering it would mean loading the slot from a validator and reading a clock.
/// That check belongs to the booking service, which already holds both the row and an injected
/// <see cref="TimeProvider"/>.
/// <para>
/// <c>PatientId</c> is not checked here either: it is optional on the wire because a booking token
/// supplies it from a claim instead, so the controller decides whether one was resolved.
/// </para>
/// </remarks>
public sealed class BookAppointmentRequestValidator : AbstractValidator<BookAppointmentRequest>
{
    public BookAppointmentRequestValidator()
    {
        RuleFor(r => r.SlotId).NotEmpty();

        RuleFor(r => r.ServiceCode)
            .Must(ServiceCodes.IsKnown)
            .WithMessage($"Service code must be one of: {string.Join(", ", ServiceCodes.All)}.")
            .When(r => r.ServiceCode is not null);
    }
}

/// <summary>Validates a <see cref="CancelAppointmentRequest"/>.</summary>
/// <remarks>
/// Only the body. Whether this appointment may be cancelled at all — its status, the cancellation
/// window — needs the row and a clock, so the lifecycle service decides it.
/// </remarks>
public sealed class CancelAppointmentRequestValidator : AbstractValidator<CancelAppointmentRequest>
{
    /// <summary>The <c>appointment_history.Reason</c> column's width.</summary>
    public const int MaxReasonLength = 500;

    public CancelAppointmentRequestValidator()
    {
        RuleFor(r => r.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("A reason for the cancellation is required.")
            .Must(reason => reason is null || reason.Trim().Length <= MaxReasonLength)
            .WithMessage($"The reason must be at most {MaxReasonLength} characters.");
    }
}

/// <summary>Validates a <see cref="RescheduleAppointmentRequest"/>.</summary>
/// <remarks>
/// Only that a slot is named. Whether it is free, in the future, the same doctor's and clear of the
/// patient's other appointments needs the rows, so the lifecycle service decides.
/// </remarks>
public sealed class RescheduleAppointmentRequestValidator : AbstractValidator<RescheduleAppointmentRequest>
{
    public RescheduleAppointmentRequestValidator()
    {
        RuleFor(r => r.NewSlotId).NotEmpty();
    }
}
