using System.Globalization;
using System.Text;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Interfaces;

namespace MediCore.Appointment.Infrastructure.Reporting;

/// <summary>
/// The utilisation report as CSV: a header block naming the filters, the clinic totals, then one
/// row per doctor.
/// </summary>
/// <remarks>
/// Rates are written as percentages to two places without a "%" sign, so a spreadsheet reads them
/// as numbers; an empty cell means there was nothing to divide by. Escaping follows the Patient
/// service's demographics export, including the guard against formula injection: a doctor's name
/// comes from Identity, and a cell starting with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> would
/// otherwise run as a formula when the file is opened.
/// </remarks>
public sealed class UtilisationCsvExporter : IUtilisationCsvExporter
{
    public byte[] Export(UtilisationReportResponse report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var filters = report.AppliedFilters;
        var csv = new StringBuilder();
        AddRow(csv, UtilisationReportText.Title);
        AddRow(csv, "Generated at (UTC)", UtilisationReportText.FormatDateTime(report.GeneratedAtUtc));
        AddRow(csv, "Period", UtilisationReportText.PeriodLabel(filters));
        AddRow(csv, "Doctor", UtilisationReportText.DoctorLabel(report));
        AddRow(csv, "Department", UtilisationReportText.DepartmentLabel(filters));
        csv.AppendLine();

        var totals = report.Totals;
        AddRow(csv, "Summary");
        AddRow(csv, "Doctors", totals.Doctors);
        AddRow(csv, "Appointments (not cancelled)", totals.Total);
        AddRow(csv, "Completed", totals.Completed);
        AddRow(csv, "No-shows", totals.NoShow);
        AddRow(csv, "Still booked", totals.Booked);
        AddRow(csv, "Cancelled", totals.Cancelled);
        AddRow(csv, "No-show rate (%)", Percent(totals.NoShowRate));
        AddRow(csv, "Slot fill (%)", Percent(totals.FillRate));
        csv.AppendLine();

        AddRow(csv, "Doctors");
        AddRow(
            csv,
            "Doctor",
            "Specialization",
            "Active",
            "Completed",
            "No-shows",
            "Still booked",
            "Cancelled",
            "Total",
            "No-show rate (%)",
            "Bookable slots",
            "Used slots",
            "Slot fill (%)");

        foreach (var doctor in report.Doctors)
        {
            AddRow(
                csv,
                doctor.DoctorName,
                doctor.Specialization,
                doctor.IsActive ? "Yes" : "No",
                doctor.Completed,
                doctor.NoShow,
                doctor.Booked,
                doctor.Cancelled,
                doctor.Total,
                Percent(doctor.NoShowRate),
                doctor.BookableSlots,
                doctor.UsedSlots,
                Percent(doctor.FillRate));
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static string Percent(decimal? rate) =>
        rate is { } value ? (value * 100m).ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;

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
}
