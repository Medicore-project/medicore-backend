using System.Globalization;
using System.Text;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Interfaces;

namespace MediCore.Patient.Infrastructure.Reporting;

public sealed class DemographicsCsvExporter : IDemographicsCsvExporter
{
    public byte[] Export(DemographicsReportResponse report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var csv = new StringBuilder();
        AddRow(csv, "MediCore demographics and visit-history report");
        AddRow(csv, "Generated at (UTC)", FormatDateTime(report.GeneratedAtUtc));
        AddRow(csv, "Age band", report.AppliedFilters.AgeBand ?? "All");
        AddRow(csv, "Gender", report.AppliedFilters.Gender ?? "All");
        AddRow(csv, "District", report.AppliedFilters.District ?? "All");
        AddRow(csv, "From", FormatDate(report.AppliedFilters.From));
        AddRow(csv, "To", FormatDate(report.AppliedFilters.To));
        csv.AppendLine();

        AddRow(csv, "Summary");
        AddRow(csv, "Total patients", report.TotalPatients);
        AddRow(csv, "Total visits", report.TotalVisits);
        AddRow(csv, "Patients with visits", report.PatientsWithVisits);
        AddRow(csv, "Patients without visits", report.PatientsWithoutVisits);
        csv.AppendLine();

        AddBreakdown(csv, "Age-band breakdown", report.AgeBands);
        AddBreakdown(csv, "Gender breakdown", report.Genders);
        AddBreakdown(csv, "District breakdown", report.Districts);

        AddRow(csv, "Visit history");
        AddRow(csv, "Period", "Visit count");
        foreach (var item in report.VisitHistory)
        {
            AddRow(csv, item.Period, item.VisitCount);
        }

        csv.AppendLine();
        AddRow(csv, "Patients");
        AddRow(
            csv,
            "Patient number",
            "Age",
            "Age band",
            "Gender",
            "District",
            "Visit count",
            "First visit (UTC)",
            "Latest visit (UTC)");

        foreach (var patient in report.Patients)
        {
            AddRow(
                csv,
                patient.PatientNumber,
                patient.Age,
                patient.AgeBand,
                patient.Gender,
                patient.District,
                patient.VisitCount,
                FormatDateTime(patient.FirstVisitAtUtc),
                FormatDateTime(patient.LatestVisitAtUtc));
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static void AddBreakdown(
        StringBuilder csv,
        string title,
        IEnumerable<DemographicsBreakdownRow> rows)
    {
        AddRow(csv, title);
        AddRow(csv, "Group", "Patient count", "Visit count");
        foreach (var row in rows)
        {
            AddRow(csv, row.Label, row.PatientCount, row.VisitCount);
        }

        csv.AppendLine();
    }

    private static void AddRow(StringBuilder csv, params object?[] values)
    {
        csv.AppendLine(string.Join(',', values.Select(value => Escape(ToInvariantString(value)))));
    }

    private static string ToInvariantString(object? value) => value switch
    {
        null => string.Empty,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string Escape(string value)
    {
        var trimmedStart = value.TrimStart();
        if (trimmedStart.Length > 0 && "=+-@".Contains(trimmedStart[0]))
        {
            value = $"'{value}";
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static string FormatDate(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "All";

    private static string FormatDateTime(DateTime? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty;
}
