using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Services;

/// <summary>Builds the doctor utilisation report (SCRUM-38).</summary>
public interface IUtilisationReportService
{
    /// <summary>
    /// Resolves the period (the current Colombo month when no dates are given), reads the counts and
    /// works out totals and rates. The filter is assumed already validated.
    /// </summary>
    Task<UtilisationReportResponse> GenerateAsync(
        UtilisationReportFilter filter,
        CancellationToken cancellationToken = default);
}
