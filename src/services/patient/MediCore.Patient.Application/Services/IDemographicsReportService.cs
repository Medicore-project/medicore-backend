using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Services;

public interface IDemographicsReportService
{
    Task<DemographicsReportResponse> GenerateAsync(
        DemographicsReportFilter filter,
        CancellationToken cancellationToken = default);
}
