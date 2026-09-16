using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Interfaces;

public interface IDemographicsReportQuery
{
    Task<IReadOnlyList<DemographicsReportSourceRow>> QueryAsync(
        DemographicsReportFilter filter,
        CancellationToken cancellationToken = default);
}
