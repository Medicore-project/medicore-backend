using FluentValidation;
using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Validators;

/// <summary>Validates a <see cref="UtilisationReportFilter"/>.</summary>
/// <remarks>
/// Only the shape. An unknown doctor or department is not an error: it simply matches no one, and
/// the report says so.
/// </remarks>
public sealed class UtilisationReportFilterValidator : AbstractValidator<UtilisationReportFilter>
{
    /// <summary>
    /// The widest period one report may cover, inclusive — a year, leap day included. Keeps a
    /// careless range from scanning every appointment the clinic has ever had.
    /// </summary>
    public const int MaxRangeDays = 366;

    /// <summary>Identity's department name column's width.</summary>
    public const int MaxDepartmentNameLength = 100;

    public UtilisationReportFilterValidator()
    {
        RuleFor(f => f.DoctorId)
            .NotEqual(Guid.Empty)
            .When(f => f.DoctorId.HasValue)
            .WithMessage("Doctor id must not be empty.");

        RuleFor(f => f.DepartmentId)
            .NotEqual(Guid.Empty)
            .When(f => f.DepartmentId.HasValue)
            .WithMessage("Department id must not be empty.");

        RuleFor(f => f.DepartmentName)
            .MaximumLength(MaxDepartmentNameLength)
            .When(f => f.DepartmentName is not null);

        RuleFor(f => f.To)
            .Must(to => to != DateOnly.MaxValue)
            .When(f => f.To.HasValue)
            .WithMessage("To date must be earlier than 9999-12-31.");

        RuleFor(f => f)
            .Must(f => f.From <= f.To)
            .When(f => f.From.HasValue && f.To.HasValue)
            .WithName(nameof(UtilisationReportFilter.To))
            .WithMessage("To date must be on or after from date.");

        RuleFor(f => f)
            .Must(f => f.To!.Value.DayNumber - f.From!.Value.DayNumber + 1 <= MaxRangeDays)
            .When(f => f.From.HasValue && f.To.HasValue && f.From <= f.To)
            .WithName(nameof(UtilisationReportFilter.To))
            .WithMessage($"The period can cover at most {MaxRangeDays} days.");
    }
}
