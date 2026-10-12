using System.Globalization;
using System.Text;
using MediCore.Billing.Application.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MediCore.Billing.Infrastructure.Reporting;

public sealed class OutstandingCsvExporter
{
    public byte[] Export(OutstandingReportResponse report)
    {
        var csv = new StringBuilder();
        Row(csv, "MediCore outstanding invoice report");
        Row(csv, "As of (Asia/Colombo)", report.AppliedFilters.AsOf);
        Row(csv, "Invoice issued from", report.AppliedFilters.From);
        Row(csv, "Invoice issued to", report.AppliedFilters.To);
        Row(csv, "Department", report.AppliedFilters.DepartmentId is null
            ? "All" : Department(report, report.AppliedFilters.DepartmentId));
        Row(csv, "Currency", report.AppliedFilters.Currency ?? "All (separate totals)");
        Row(csv, "Generated at (UTC)", report.GeneratedAtUtc.ToString("O"));
        Row(csv, "Age is measured from the date an invoice became payable.");
        csv.AppendLine();
        Row(csv, "Currency totals");
        Row(csv, "Currency", "Balance due", "Invoices");
        foreach (var total in report.TotalsByCurrency)
            Row(csv, total.Currency, total.BalanceDue, total.InvoiceCount);
        csv.AppendLine();
        Row(csv, "Ageing by department");
        Row(csv, "Department", "Department ID", "Currency", "Age (days)", "Balance due", "Invoices");
        foreach (var bucket in report.Buckets)
            Row(csv, Department(report, bucket.DepartmentId), bucket.DepartmentId, bucket.Currency,
                bucket.Bucket, bucket.BalanceDue, bucket.InvoiceCount);
        csv.AppendLine();
        Row(csv, "Unpaid invoices");
        Row(csv, "Invoice", "Invoice ID", "Department", "Currency", "Issued date", "Payable date",
            "Total", "Paid", "Balance due", "Age (days)", "Bucket");
        foreach (var invoice in report.Invoices)
            Row(csv, invoice.InvoiceNumber, invoice.InvoiceId, Department(report, invoice.DepartmentId),
                invoice.Currency, invoice.IssuedDate, invoice.FinalizedDate, invoice.Total,
                invoice.AmountPaid, invoice.BalanceDue, invoice.AgeDays, invoice.Bucket);
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    internal static string Department(OutstandingReportResponse report, Guid? id) => id is null
        ? "Unassigned (legacy booking)"
        : id == report.AppliedFilters.DepartmentId && !string.IsNullOrWhiteSpace(report.AppliedFilters.DepartmentName)
            ? report.AppliedFilters.DepartmentName!
            : id.Value.ToString();

    private static void Row(StringBuilder csv, params object?[] cells) =>
        csv.AppendLine(string.Join(',', cells.Select(cell => Escape(cell switch
        {
            null => string.Empty,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            _ => cell.ToString() ?? string.Empty
        }))));

    private static string Escape(string value)
    {
        if (value.TrimStart() is { Length: > 0 } trimmed && "=+-@".Contains(trimmed[0]))
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}

public sealed class OutstandingPdfExporter
{
    public byte[] Export(OutstandingReportResponse report)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var department = report.AppliedFilters.DepartmentId is null
            ? "All" : OutstandingCsvExporter.Department(report, report.AppliedFilters.DepartmentId);
        var issuedPeriod = $"{report.AppliedFilters.From?.ToString("yyyy-MM-dd") ?? "Any"} to " +
            (report.AppliedFilters.To?.ToString("yyyy-MM-dd") ?? "Any");
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(x => x.FontSize(9));
            page.Header().Text("MediCore outstanding invoice report")
                .FontSize(19).SemiBold().FontColor(Colors.Blue.Darken2);
            page.Content().PaddingVertical(12).Column(column =>
            {
                column.Spacing(10);
                column.Item().Text($"As of {report.AppliedFilters.AsOf:yyyy-MM-dd} (Asia/Colombo)  |  Issued: {issuedPeriod}  |  Department: {department}  |  Currency: {report.AppliedFilters.Currency ?? "All"}");
                column.Item().Text("Only payable invoices with a remaining balance are included. Age starts when an invoice becomes payable; currencies are never combined.")
                    .FontColor(Colors.Grey.Darken1);
                column.Item().Text("Totals by currency").FontSize(12).SemiBold();
                foreach (var total in report.TotalsByCurrency)
                    column.Item().Text($"{total.Currency} {total.BalanceDue:N2}  |  {total.InvoiceCount} invoices");
                if (report.TotalsByCurrency.Count == 0)
                    column.Item().Text("No outstanding invoices match these filters.");
                column.Item().Text("Ageing by department").FontSize(12).SemiBold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(4);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                    });
                    table.Header(header =>
                    {
                        foreach (var heading in new[] { "Department", "Currency", "Days", "Balance due", "Invoices" })
                            header.Cell().Background(Colors.Blue.Lighten4).Padding(5).Text(heading).SemiBold();
                    });
                    foreach (var bucket in report.Buckets)
                    {
                        table.Cell().Padding(5).Text(OutstandingCsvExporter.Department(report, bucket.DepartmentId));
                        table.Cell().Padding(5).Text(bucket.Currency);
                        table.Cell().Padding(5).Text(bucket.Bucket);
                        table.Cell().Padding(5).Text(bucket.BalanceDue.ToString("N2", CultureInfo.InvariantCulture));
                        table.Cell().Padding(5).Text(bucket.InvoiceCount.ToString(CultureInfo.InvariantCulture));
                    }
                });
                column.Item().Text("Unpaid invoices").FontSize(12).SemiBold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                    });
                    table.Header(header =>
                    {
                        foreach (var heading in new[] { "Invoice", "Payable date", "Currency", "Total", "Paid", "Balance due", "Days" })
                            header.Cell().Background(Colors.Blue.Lighten4).Padding(5).Text(heading).SemiBold();
                    });
                    foreach (var invoice in report.Invoices)
                    {
                        table.Cell().Padding(5).Text(invoice.InvoiceNumber);
                        table.Cell().Padding(5).Text(invoice.FinalizedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                        table.Cell().Padding(5).Text(invoice.Currency);
                        table.Cell().Padding(5).Text(invoice.Total.ToString("N2", CultureInfo.InvariantCulture));
                        table.Cell().Padding(5).Text(invoice.AmountPaid.ToString("N2", CultureInfo.InvariantCulture));
                        table.Cell().Padding(5).Text(invoice.BalanceDue.ToString("N2", CultureInfo.InvariantCulture));
                        table.Cell().Padding(5).Text(invoice.AgeDays.ToString(CultureInfo.InvariantCulture));
                    }
                });
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        })).GeneratePdf();
    }
}
