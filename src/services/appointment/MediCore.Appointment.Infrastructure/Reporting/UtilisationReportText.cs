using System.Globalization;
using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Infrastructure.Reporting;

/// <summary>
/// The words and number formats both utilisation exports share, so the CSV and the PDF never
/// describe the same report differently.
/// </summary>
internal static class UtilisationReportText
{
    public const string Title = "MediCore doctor utilisation report";

    /// <summary>
    /// The doctor filter as a name. The report's own row supplies it when the doctor is in the
    /// result; otherwise (a doctor with nothing in scope) the id is all there is.
    /// </summary>
    public static string DoctorLabel(UtilisationReportResponse report) =>
        report.AppliedFilters.DoctorId is not { } doctorId
            ? "All"
            : report.Doctors.FirstOrDefault(d => d.DoctorId == doctorId)?.DoctorName ?? doctorId.ToString();

    /// <summary>
    /// The department filter as the page named it. This service holds only the id, so the id stands
    /// in when no name was sent.
    /// </summary>
    public static string DepartmentLabel(AppliedUtilisationFilters filters) =>
        filters.DepartmentId is not { } departmentId
            ? "All"
            : filters.DepartmentName ?? departmentId.ToString();

    public static string PeriodLabel(AppliedUtilisationFilters filters) =>
        $"{FormatDate(filters.From)} to {FormatDate(filters.To)}"
        + (filters.IsDefaultPeriod ? " (current month)" : string.Empty);

    public static string FormatDate(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatDateTime(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A 0–1 rate as a percentage to one place, or "-" when there was nothing to divide by.</summary>
    public static string FormatPercent(decimal? rate) =>
        rate is { } value
            ? (value * 100m).ToString("0.0", CultureInfo.InvariantCulture) + "%"
            : "-";
}
