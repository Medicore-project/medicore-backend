using System.Globalization;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MediCore.Appointment.Infrastructure.Reporting;

/// <summary>
/// The utilisation report as an A4 landscape PDF, laid out like the Patient service's demographics
/// report: filters, summary cards, then the doctor table.
/// </summary>
public sealed class UtilisationPdfExporter : IUtilisationPdfExporter
{
    public byte[] Export(UtilisationReportResponse report)
    {
        ArgumentNullException.ThrowIfNull(report);
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(style => style.FontSize(8));

                page.Header().Column(header =>
                {
                    header.Item().Text(UtilisationReportText.Title)
                        .FontSize(18)
                        .SemiBold()
                        .FontColor(Colors.Blue.Darken2);
                    header.Item().Text(
                        $"Generated at {UtilisationReportText.FormatDateTime(report.GeneratedAtUtc)} UTC")
                        .FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(12).Column(content =>
                {
                    content.Spacing(10);
                    content.Item().Element(container => ComposeFilters(container, report));
                    content.Item().Element(container => ComposeSummary(container, report.Totals));
                    content.Item().Element(container => ComposeDoctors(container, report));
                    content.Item().Text(
                        "Total counts every appointment that was not cancelled. No-show rate is no-shows over "
                        + "completed plus no-shows; still-booked appointments are left out. Slot fill is used "
                        + "slots over slots not blocked.")
                        .FontColor(Colors.Grey.Darken1);
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeFilters(IContainer container, UtilisationReportResponse report)
    {
        var filters = report.AppliedFilters;
        container.Column(column =>
        {
            column.Item().Text("Applied filters").FontSize(11).SemiBold();
            column.Item().Text(
                $"Period: {UtilisationReportText.PeriodLabel(filters)}   |   " +
                $"Doctor: {UtilisationReportText.DoctorLabel(report)}   |   " +
                $"Department: {UtilisationReportText.DepartmentLabel(filters)}");
        });
    }

    private static void ComposeSummary(IContainer container, UtilisationTotals totals)
    {
        container.Row(row =>
        {
            row.Spacing(8);
            row.RelativeItem().Element(card => ComposeSummaryCard(card, "Appointments", Count(totals.Total)));
            row.RelativeItem().Element(card => ComposeSummaryCard(card, "Completed", Count(totals.Completed)));
            row.RelativeItem().Element(card => ComposeSummaryCard(card, "No-shows", Count(totals.NoShow)));
            row.RelativeItem().Element(card => ComposeSummaryCard(
                card, "No-show rate", UtilisationReportText.FormatPercent(totals.NoShowRate)));
            row.RelativeItem().Element(card => ComposeSummaryCard(
                card, "Slot fill", UtilisationReportText.FormatPercent(totals.FillRate)));
        });
    }

    private static void ComposeSummaryCard(IContainer container, string label, string value)
    {
        container
            .Background(Colors.Blue.Lighten5)
            .Border(1)
            .BorderColor(Colors.Blue.Lighten3)
            .Padding(8)
            .Column(column =>
            {
                column.Item().Text(value).FontSize(15).SemiBold();
                column.Item().Text(label).FontColor(Colors.Grey.Darken1);
            });
    }

    private static void ComposeDoctors(IContainer container, UtilisationReportResponse report)
    {
        container.Column(column =>
        {
            column.Item().Text("Doctors").FontSize(11).SemiBold();
            if (report.Doctors.Count == 0)
            {
                column.Item().Text("No doctors match the selected filters.").Italic();
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2f);
                    columns.RelativeColumn(1.6f);
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Element(TableHeader).Text("Doctor");
                    header.Cell().Element(TableHeader).Text("Specialization");
                    header.Cell().Element(TableHeader).AlignRight().Text("Completed");
                    header.Cell().Element(TableHeader).AlignRight().Text("No-shows");
                    header.Cell().Element(TableHeader).AlignRight().Text("Still booked");
                    header.Cell().Element(TableHeader).AlignRight().Text("Cancelled");
                    header.Cell().Element(TableHeader).AlignRight().Text("Total");
                    header.Cell().Element(TableHeader).AlignRight().Text("No-show rate");
                    header.Cell().Element(TableHeader).AlignRight().Text("Slots used");
                    header.Cell().Element(TableHeader).AlignRight().Text("Slot fill");
                });

                foreach (var doctor in report.Doctors)
                {
                    table.Cell().Element(TableCell).Text(
                        doctor.IsActive ? doctor.DoctorName : $"{doctor.DoctorName} (inactive)");
                    table.Cell().Element(TableCell).Text(
                        string.IsNullOrWhiteSpace(doctor.Specialization) ? "-" : doctor.Specialization);
                    table.Cell().Element(TableCell).AlignRight().Text(Count(doctor.Completed));
                    table.Cell().Element(TableCell).AlignRight().Text(Count(doctor.NoShow));
                    table.Cell().Element(TableCell).AlignRight().Text(Count(doctor.Booked));
                    table.Cell().Element(TableCell).AlignRight().Text(Count(doctor.Cancelled));
                    table.Cell().Element(TableCell).AlignRight().Text(Count(doctor.Total)).SemiBold();
                    table.Cell().Element(TableCell).AlignRight().Text(
                        UtilisationReportText.FormatPercent(doctor.NoShowRate));
                    table.Cell().Element(TableCell).AlignRight().Text(
                        $"{Count(doctor.UsedSlots)} / {Count(doctor.BookableSlots)}");
                    table.Cell().Element(TableCell).AlignRight().Text(
                        UtilisationReportText.FormatPercent(doctor.FillRate));
                }
            });
        });
    }

    private static IContainer TableHeader(IContainer container) => container
        .Background(Colors.Grey.Lighten3)
        .BorderBottom(1)
        .BorderColor(Colors.Grey.Lighten1)
        .Padding(4)
        .DefaultTextStyle(style => style.SemiBold());

    private static IContainer TableCell(IContainer container) => container
        .BorderBottom(1)
        .BorderColor(Colors.Grey.Lighten3)
        .Padding(4);

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
