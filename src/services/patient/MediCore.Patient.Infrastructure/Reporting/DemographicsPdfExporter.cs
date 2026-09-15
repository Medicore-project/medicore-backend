using System.Globalization;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MediCore.Patient.Infrastructure.Reporting;

public sealed class DemographicsPdfExporter : IDemographicsPdfExporter
{
    public byte[] Export(DemographicsReportResponse report)
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
                    header.Item().Text("MediCore demographics and visit-history report")
                        .FontSize(18)
                        .SemiBold()
                        .FontColor(Colors.Blue.Darken2);
                    header.Item().Text(
                        $"Generated at {FormatDateTime(report.GeneratedAtUtc)} UTC")
                        .FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(12).Column(content =>
                {
                    content.Spacing(10);
                    content.Item().Element(container => ComposeFilters(container, report.AppliedFilters));
                    content.Item().Element(container => ComposeSummary(container, report));
                    content.Item().Element(container => ComposeBreakdowns(container, report));
                    content.Item().Element(container => ComposeVisitHistory(container, report.VisitHistory));
                    content.Item().Element(container => ComposePatients(container, report.Patients));
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

    private static void ComposeFilters(IContainer container, AppliedDemographicsFilters filters)
    {
        container.Column(column =>
        {
            column.Item().Text("Applied filters").FontSize(11).SemiBold();
            column.Item().Text(
                $"Age band: {filters.AgeBand ?? "All"}   |   " +
                $"Gender: {filters.Gender ?? "All"}   |   " +
                $"District: {filters.District ?? "All"}   |   " +
                $"Date range: {FormatDate(filters.From)} to {FormatDate(filters.To)}");
        });
    }

    private static void ComposeSummary(IContainer container, DemographicsReportResponse report)
    {
        container.Row(row =>
        {
            row.Spacing(8);
            row.RelativeItem().Element(card => ComposeSummaryCard(card, "Patients", report.TotalPatients));
            row.RelativeItem().Element(card => ComposeSummaryCard(card, "Visits", report.TotalVisits));
            row.RelativeItem().Element(card => ComposeSummaryCard(card, "With visits", report.PatientsWithVisits));
            row.RelativeItem().Element(card => ComposeSummaryCard(card, "Without visits", report.PatientsWithoutVisits));
        });
    }

    private static void ComposeSummaryCard(IContainer container, string label, int value)
    {
        container
            .Background(Colors.Blue.Lighten5)
            .Border(1)
            .BorderColor(Colors.Blue.Lighten3)
            .Padding(8)
            .Column(column =>
            {
                column.Item().Text(value.ToString(CultureInfo.InvariantCulture)).FontSize(15).SemiBold();
                column.Item().Text(label).FontColor(Colors.Grey.Darken1);
            });
    }

    private static void ComposeBreakdowns(IContainer container, DemographicsReportResponse report)
    {
        container.Row(row =>
        {
            row.Spacing(12);
            row.RelativeItem().Element(item => ComposeBreakdown(item, "Age bands", report.AgeBands));
            row.RelativeItem().Element(item => ComposeBreakdown(item, "Genders", report.Genders));
            row.RelativeItem().Element(item => ComposeBreakdown(item, "Districts", report.Districts));
        });
    }

    private static void ComposeBreakdown(
        IContainer container,
        string title,
        IReadOnlyList<DemographicsBreakdownRow> rows)
    {
        container.Column(column =>
        {
            column.Item().Text(title).FontSize(11).SemiBold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Element(TableHeader).Text("Group");
                    header.Cell().Element(TableHeader).AlignRight().Text("Patients");
                    header.Cell().Element(TableHeader).AlignRight().Text("Visits");
                });

                foreach (var item in rows)
                {
                    table.Cell().Element(TableCell).Text(item.Label);
                    table.Cell().Element(TableCell).AlignRight().Text(
                        item.PatientCount.ToString(CultureInfo.InvariantCulture));
                    table.Cell().Element(TableCell).AlignRight().Text(
                        item.VisitCount.ToString(CultureInfo.InvariantCulture));
                }
            });
        });
    }

    private static void ComposeVisitHistory(
        IContainer container,
        IReadOnlyList<VisitHistoryBucket> history)
    {
        container.Column(column =>
        {
            column.Item().Text("Visit history by month").FontSize(11).SemiBold();
            if (history.Count == 0)
            {
                column.Item().Text("No visits match the selected filters.").Italic();
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                table.Header(header =>
                {
                    header.Cell().Element(TableHeader).Text("Month");
                    header.Cell().Element(TableHeader).AlignRight().Text("Visits");
                });
                foreach (var item in history)
                {
                    table.Cell().Element(TableCell).Text(item.Period);
                    table.Cell().Element(TableCell).AlignRight().Text(
                        item.VisitCount.ToString(CultureInfo.InvariantCulture));
                }
            });
        });
    }

    private static void ComposePatients(
        IContainer container,
        IReadOnlyList<DemographicsPatientSummary> patients)
    {
        container.Column(column =>
        {
            column.Item().Text("Patient summary").FontSize(11).SemiBold();
            if (patients.Count == 0)
            {
                column.Item().Text("No active patients match the selected filters.").Italic();
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.4f);
                    columns.RelativeColumn(.5f);
                    columns.RelativeColumn(.7f);
                    columns.RelativeColumn(.8f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(.6f);
                    columns.RelativeColumn(1.25f);
                    columns.RelativeColumn(1.25f);
                });

                table.Header(header =>
                {
                    header.Cell().Element(TableHeader).Text("Patient no.");
                    header.Cell().Element(TableHeader).Text("Age");
                    header.Cell().Element(TableHeader).Text("Band");
                    header.Cell().Element(TableHeader).Text("Gender");
                    header.Cell().Element(TableHeader).Text("District");
                    header.Cell().Element(TableHeader).Text("Visits");
                    header.Cell().Element(TableHeader).Text("First visit");
                    header.Cell().Element(TableHeader).Text("Latest visit");
                });

                foreach (var patient in patients)
                {
                    table.Cell().Element(TableCell).Text(patient.PatientNumber);
                    table.Cell().Element(TableCell).Text(
                        patient.Age.ToString(CultureInfo.InvariantCulture));
                    table.Cell().Element(TableCell).Text(patient.AgeBand);
                    table.Cell().Element(TableCell).Text(patient.Gender);
                    table.Cell().Element(TableCell).Text(patient.District);
                    table.Cell().Element(TableCell).Text(
                        patient.VisitCount.ToString(CultureInfo.InvariantCulture));
                    table.Cell().Element(TableCell).Text(FormatDateTime(patient.FirstVisitAtUtc));
                    table.Cell().Element(TableCell).Text(FormatDateTime(patient.LatestVisitAtUtc));
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

    private static string FormatDate(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "All";

    private static string FormatDateTime(DateTime? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-";
}
