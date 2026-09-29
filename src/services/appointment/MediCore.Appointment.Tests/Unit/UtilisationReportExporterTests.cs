using System.Text;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Infrastructure.Reporting;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>SCRUM-38 AC4: the utilisation report's CSV and PDF exports.</summary>
public sealed class UtilisationReportExporterTests
{
    private static readonly DateTime GeneratedAt = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherDoctorId = Guid.Parse("11111111-1111-1111-1111-222222222222");
    private static readonly Guid DepartmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // ── CSV ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Csv_names_the_filters_and_the_default_period()
    {
        var lines = CsvLines(Report());

        Assert.Equal("MediCore doctor utilisation report", lines[0]);
        Assert.Contains("Generated at (UTC),2026-09-23 08:00", lines);
        Assert.Contains("Period,2026-09-01 to 2026-09-30 (current month)", lines);
        Assert.Contains("Doctor,All", lines);
        Assert.Contains("Department,All", lines);
    }

    [Fact]
    public void Csv_uses_the_department_name_the_page_sent_and_the_doctors_own_name()
    {
        var report = Report(filters: Filters(doctorId: DoctorId, departmentId: DepartmentId, departmentName: "Cardiology"));

        var lines = CsvLines(report);

        Assert.Contains("Doctor,Dr. Perera", lines);
        Assert.Contains("Department,Cardiology", lines);
    }

    [Fact]
    public void Csv_falls_back_to_the_ids_when_there_is_no_name()
    {
        var missingDoctor = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var report = Report(filters: Filters(doctorId: missingDoctor, departmentId: DepartmentId));

        var lines = CsvLines(report);

        Assert.Contains($"Doctor,{missingDoctor}", lines);
        Assert.Contains($"Department,{DepartmentId}", lines);
    }

    [Fact]
    public void Csv_has_the_totals_and_a_row_per_doctor_with_rates_as_plain_percentages()
    {
        var lines = CsvLines(Report());

        Assert.Contains("Appointments (not cancelled),15", lines);
        Assert.Contains("No-show rate (%),30.00", lines);
        Assert.Contains("Slot fill (%),75.00", lines);
        Assert.Contains(
            "Doctor,Specialization,Active,Completed,No-shows,Still booked,Cancelled,Total,No-show rate (%),Bookable slots,Used slots,Slot fill (%)",
            lines);
        Assert.Contains("Dr. Perera,Cardiology,Yes,7,3,5,4,15,30.00,20,15,75.00", lines);
    }

    [Fact]
    public void Csv_leaves_a_rate_empty_when_there_was_nothing_to_divide_by()
    {
        var doctor = Doctor(OtherDoctorId, "Dr. Silva", isActive: false) with
        {
            Completed = 0, NoShow = 0, Booked = 2, Cancelled = 0, Total = 2,
            NoShowRate = null, BookableSlots = 0, UsedSlots = 0, FillRate = null
        };

        var lines = CsvLines(Report(doctors: [doctor]));

        Assert.Contains("Dr. Silva,Cardiology,No,0,0,2,0,2,,0,0,", lines);
    }

    [Fact]
    public void Csv_neutralises_formulas_and_quotes_commas_in_names()
    {
        var doctor = Doctor(DoctorId, "=HYPERLINK(\"http://evil\")") with { Specialization = "Ear, Nose & Throat" };

        var csv = CsvText(Report(doctors: [doctor]));

        Assert.Contains("\"'=HYPERLINK(\"\"http://evil\"\")\"", csv);
        Assert.Contains("\"Ear, Nose & Throat\"", csv);
        Assert.DoesNotContain("\n=HYPERLINK", csv);
    }

    // ── PDF ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Pdf_export_produces_a_valid_pdf_document()
    {
        var bytes = new UtilisationPdfExporter().Export(Report());

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public void Pdf_export_handles_an_empty_report()
    {
        var bytes = new UtilisationPdfExporter().Export(Report(doctors: []));

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    // ── Text helpers ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.3, "30.0%")]
    [InlineData(0.3333, "33.3%")]
    [InlineData(1.0, "100.0%")]
    [InlineData(null, "-")]
    public void Percentages_read_to_one_place_and_a_missing_rate_is_a_dash(double? rate, string expected)
    {
        Assert.Equal(expected, UtilisationReportText.FormatPercent(rate is null ? null : (decimal)rate));
    }

    [Fact]
    public void A_chosen_period_is_not_called_the_current_month()
    {
        var filters = Filters() with { From = new DateOnly(2026, 7, 1), To = new DateOnly(2026, 7, 31), IsDefaultPeriod = false };

        Assert.Equal("2026-07-01 to 2026-07-31", UtilisationReportText.PeriodLabel(filters));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string CsvText(UtilisationReportResponse report) =>
        Encoding.UTF8.GetString(new UtilisationCsvExporter().Export(report));

    private static string[] CsvLines(UtilisationReportResponse report) =>
        CsvText(report).Split(["\r\n", "\n"], StringSplitOptions.None);

    private static AppliedUtilisationFilters Filters(
        Guid? doctorId = null,
        Guid? departmentId = null,
        string? departmentName = null) =>
        new(doctorId, departmentId, departmentName, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), IsDefaultPeriod: true);

    private static DoctorUtilisationRow Doctor(Guid id, string name, bool isActive = true) => new(
        id, name, "Cardiology", DepartmentId, isActive,
        Completed: 7, NoShow: 3, Cancelled: 4, Booked: 5, Total: 15, NoShowRate: 0.3m,
        BookableSlots: 20, UsedSlots: 15, FillRate: 0.75m);

    private static UtilisationReportResponse Report(
        AppliedUtilisationFilters? filters = null,
        IReadOnlyList<DoctorUtilisationRow>? doctors = null)
    {
        doctors ??= [Doctor(DoctorId, "Dr. Perera")];

        return new UtilisationReportResponse(
            GeneratedAt,
            filters ?? Filters(),
            new UtilisationTotals(
                Doctors: doctors.Count,
                Completed: 7, NoShow: 3, Cancelled: 4, Booked: 5, Total: 15, NoShowRate: 0.3m,
                BookableSlots: 20, UsedSlots: 15, FillRate: 0.75m),
            doctors);
    }
}
