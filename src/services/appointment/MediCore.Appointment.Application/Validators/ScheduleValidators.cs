using FluentValidation;
using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Validators;

/// <summary>
/// Slot lengths the clinic supports. Fixed rather than free-form so that a day divides into a
/// predictable grid and the UI can render a week without ragged columns.
/// </summary>
file static class AllowedSlotDurations
{
    internal static readonly int[] Values = [15, 30];
}

/// <summary>Validates a <see cref="CreateDoctorScheduleRequest"/>.</summary>
public sealed class CreateDoctorScheduleRequestValidator
    : AbstractValidator<CreateDoctorScheduleRequest>
{
    public CreateDoctorScheduleRequestValidator()
    {
        RuleFor(r => r.DoctorId).NotEmpty();

        RuleFor(r => r.DayOfWeek).IsInEnum();

        RuleFor(r => r.SlotDurationMinutes)
            .Must(d => AllowedSlotDurations.Values.Contains(d))
            .WithMessage($"Slot duration must be one of: {string.Join(", ", AllowedSlotDurations.Values)} minutes.");

        RuleFor(r => r.EndTime)
            .GreaterThan(r => r.StartTime)
            .WithMessage("End time must be after start time.");

        // A window shorter than one slot would silently produce an empty day, which looks like the
        // schedule simply did not work. Rejecting it up front says why.
        RuleFor(r => r)
            .Must(FitsAtLeastOneSlot)
            .WithName(nameof(CreateDoctorScheduleRequest.EndTime))
            .WithMessage("The working window is shorter than a single slot.")
            .When(r => r.EndTime > r.StartTime && AllowedSlotDurations.Values.Contains(r.SlotDurationMinutes));

        RuleFor(r => r.EffectiveTo)
            .GreaterThanOrEqualTo(r => r.EffectiveFrom)
            .When(r => r.EffectiveTo.HasValue)
            .WithMessage("Effective-to date must not be before the effective-from date.");
    }

    private static bool FitsAtLeastOneSlot(CreateDoctorScheduleRequest request) =>
        (request.EndTime.ToTimeSpan() - request.StartTime.ToTimeSpan()).TotalMinutes
        >= request.SlotDurationMinutes;
}

/// <summary>Validates an <see cref="UpdateDoctorScheduleRequest"/>.</summary>
public sealed class UpdateDoctorScheduleRequestValidator
    : AbstractValidator<UpdateDoctorScheduleRequest>
{
    public UpdateDoctorScheduleRequestValidator()
    {
        RuleFor(r => r.SlotDurationMinutes)
            .Must(d => AllowedSlotDurations.Values.Contains(d))
            .WithMessage($"Slot duration must be one of: {string.Join(", ", AllowedSlotDurations.Values)} minutes.");

        RuleFor(r => r.EndTime)
            .GreaterThan(r => r.StartTime)
            .WithMessage("End time must be after start time.");

        RuleFor(r => r)
            .Must(FitsAtLeastOneSlot)
            .WithName(nameof(UpdateDoctorScheduleRequest.EndTime))
            .WithMessage("The working window is shorter than a single slot.")
            .When(r => r.EndTime > r.StartTime && AllowedSlotDurations.Values.Contains(r.SlotDurationMinutes));

        RuleFor(r => r.EffectiveTo)
            .GreaterThanOrEqualTo(r => r.EffectiveFrom)
            .When(r => r.EffectiveTo.HasValue)
            .WithMessage("Effective-to date must not be before the effective-from date.");
    }

    private static bool FitsAtLeastOneSlot(UpdateDoctorScheduleRequest request) =>
        (request.EndTime.ToTimeSpan() - request.StartTime.ToTimeSpan()).TotalMinutes
        >= request.SlotDurationMinutes;
}

/// <summary>Validates a <see cref="BlockSlotRequest"/>.</summary>
public sealed class BlockSlotRequestValidator : AbstractValidator<BlockSlotRequest>
{
    public BlockSlotRequestValidator()
    {
        RuleFor(r => r.Reason)
            .MaximumLength(500)
            .When(r => r.Reason is not null);
    }
}
