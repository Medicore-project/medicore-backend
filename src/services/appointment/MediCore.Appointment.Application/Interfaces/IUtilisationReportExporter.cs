using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>Renders the utilisation report as CSV (SCRUM-38).</summary>
public interface IUtilisationCsvExporter
{
    byte[] Export(UtilisationReportResponse report);
}

/// <summary>Renders the utilisation report as PDF (SCRUM-38).</summary>
public interface IUtilisationPdfExporter
{
    byte[] Export(UtilisationReportResponse report);
}
