using System.Security.Claims;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// Shared plumbing for the appointment controllers: explicit validation responses and the identity
/// of the caller.
/// </summary>
/// <remarks>
/// The Patient service repeats these helpers in each controller. They are pulled up here instead
/// because this service has four controllers that all need them and the duplication bought nothing.
/// </remarks>
public abstract class AppointmentControllerBase : ControllerBase
{
    /// <summary>
    /// Who is acting, for the <c>CreatedBy</c> / <c>UpdatedBy</c> / <c>ReviewedBy</c> audit
    /// columns. Prefers email because it is the form a human reading the audit trail recognises.
    /// </summary>
    protected string CurrentActor() =>
        User.FindFirstValue(ClaimTypes.Email)
        ?? User.FindFirstValue("email")
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? User.Identity?.Name
        ?? "system";

    /// <summary>
    /// The caller's own Staff/Doctor id, from the <c>staffId</c> claim the identity service embeds
    /// for staff accounts — the same id stored as <c>DoctorLeave.DoctorId</c> and friends. Null when
    /// the caller has no staff profile (or an older token predating the claim); treat that as "this
    /// caller cannot own anything", never as a wildcard match.
    /// </summary>
    protected Guid? CurrentStaffId() =>
        Guid.TryParse(User.FindFirstValue("staffId"), out var staffId) ? staffId : null;

    /// <summary>
    /// Builds a 400 from FluentValidation failures, keyed by camelCased property name so the
    /// payload matches the JSON the client sent.
    /// </summary>
    protected IActionResult CreateValidationProblem(
        IEnumerable<ValidationFailure> failures,
        string title)
    {
        var errors = failures
            .GroupBy(e => ToCamelCase(e.PropertyName))
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        return ValidationProblem(new ValidationProblemDetails(errors)
        {
            Title = title,
            Status = StatusCodes.Status400BadRequest
        });
    }

    /// <summary>Builds a 409 with a human-readable title.</summary>
    protected IActionResult ConflictProblem(string title) =>
        Conflict(new ProblemDetails
        {
            Title = title,
            Status = StatusCodes.Status409Conflict
        });

    /// <summary>Builds a 403 with a human-readable title.</summary>
    protected IActionResult ForbiddenProblem(string title) =>
        StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
        {
            Title = title,
            Status = StatusCodes.Status403Forbidden
        });

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
