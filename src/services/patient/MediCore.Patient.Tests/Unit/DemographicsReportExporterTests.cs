using System.Text;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Infrastructure.Reporting;

namespace MediCore.Patient.Tests.Unit;

public sealed class DemographicsReportExporterTests
{
    [Fact]
    public void Csv_contains_filters_summaries_and_patient_rows()
    {
        var csv = Encoding.UTF8.GetString(new DemographicsCsvExporter().Export(Report()));

        Assert.Contains("MediCore demographics and visit-history report", csv);
        Assert.Contains("Age band,18-34", csv);
        Assert.Contains("District,Colombo", csv);
        Assert.Contains("Total patients,1", csv);
        Assert.Contains("Visit history", csv);
        Assert.Contains("PAT-000001,31,18-34,Female,Colombo,1", csv);
    }

    [Fact]
    public void Csv_escapes_rfc4180_values_and_neutralizes_spreadsheet_formulas()
    {
        var source = Report();
        var unsafePatient = source.Patients[0] with
        {
            PatientNumber = "=HYPERLINK(\"bad\")",
            District = "Colombo, West"
        };
        var report = source with { Patients = [unsafePatient] };

        var csv = Encoding.UTF8.GetString(new DemographicsCsvExporter().Export(report));

        Assert.Contains("\"'=HYPERLINK(\"\"bad\"\")\"", csv);
        Assert.Contains("\"Colombo, West\"", csv);
    }

    [Fact]
    public void Pdf_export_produces_a_valid_pdf_document()
    {
        var bytes = new DemographicsPdfExporter().Export(Report());

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    private static DemographicsReportResponse Report()
    {
        var patient = new DemographicsPatientSummary(
            Guid.NewGuid(),
            "PAT-000001",
            31,
            "18-34",
            "Female",
            "Colombo",
            1,
            new DateTime(2026, 1, 3, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 3, 9, 0, 0, DateTimeKind.Utc));

        return new DemographicsReportResponse(
            new DateTime(2026, 9, 14, 10, 30, 0, DateTimeKind.Utc),
            new AppliedDemographicsFilters(
                "18-34",
                "Female",
                "Colombo",
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 31)),
            1,
            1,
            1,
            0,
            [new DemographicsBreakdownRow("18-34", 1, 1)],
            [new DemographicsBreakdownRow("Female", 1, 1)],
            [new DemographicsBreakdownRow("Colombo", 1, 1)],
            [new VisitHistoryBucket("2026-01", new DateOnly(2026, 1, 1), 1)],
            [patient]);
    }
}
