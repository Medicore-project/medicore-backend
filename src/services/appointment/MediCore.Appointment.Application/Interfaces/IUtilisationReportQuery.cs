using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>
/// Reads each doctor's appointment and slot counts for a period (SCRUM-38).
/// </summary>
/// <remarks>
/// Implemented with raw ADO.NET rather than EF LINQ, as the module requires of report queries, with
/// every value bound as a parameter.
/// </remarks>
public interface IUtilisationReportQuery
{
    /// <summary>
    /// One row per doctor in scope: every active doctor, plus any inactive one with appointments or
    /// slots in the period.
    /// </summary>
    Task<IReadOnlyList<UtilisationSourceRow>> QueryAsync(
        UtilisationQueryCriteria criteria,
        CancellationToken cancellationToken = default);
}
