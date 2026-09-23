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
