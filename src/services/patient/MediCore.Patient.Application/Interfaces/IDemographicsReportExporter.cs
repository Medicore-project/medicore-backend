using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Interfaces;

public interface IDemographicsCsvExporter
{
    byte[] Export(DemographicsReportResponse report);
}

public interface IDemographicsPdfExporter
{
    byte[] Export(DemographicsReportResponse report);
}
