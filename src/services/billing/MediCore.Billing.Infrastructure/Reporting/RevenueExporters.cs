using System.Globalization;
using System.Text;
using MediCore.Billing.Application.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MediCore.Billing.Infrastructure.Reporting;

public sealed class RevenueCsvExporter
{
    public byte[] Export(RevenueReportResponse report)
    {
        var csv = new StringBuilder();
        Row(csv, "MediCore revenue report");
        Row(csv, "Period (Asia/Colombo)", report.AppliedFilters.From, report.AppliedFilters.To);
        Row(csv, "Department", report.AppliedFilters.DepartmentId is null
            ? "All" : Department(report, report.AppliedFilters.DepartmentId));
        Row(csv, "Payment method", report.AppliedFilters.PaymentMethod ?? "All");
        Row(csv, "Currency", report.AppliedFilters.Currency ?? "All (separate totals)");
        Row(csv, "Generated at (UTC)", report.GeneratedAtUtc.ToString("O"));
        csv.AppendLine();
        Row(csv, "Currency totals");
        Row(csv, "Currency", "Amount", "Payments");
        foreach (var total in report.TotalsByCurrency)
            Row(csv, total.Currency, total.Amount, total.PaymentCount);
        csv.AppendLine();
        Row(csv, "Revenue by department and payment method");
        Row(csv, "Department", "Department ID", "Payment method", "Currency", "Amount", "Payments");
        foreach (var row in report.Breakdown)
            Row(csv, Department(report, row.DepartmentId), row.DepartmentId, row.PaymentMethod,
                row.Currency, row.Amount, row.PaymentCount);
        csv.AppendLine();
        Row(csv, "Daily revenue");
        Row(csv, "Colombo date", "Currency", "Amount", "Payments");
        foreach (var row in report.DailyTotals)
            Row(csv, row.Date, row.Currency, row.Amount, row.PaymentCount);
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    internal static string Department(RevenueReportResponse report, Guid? id) => id is null
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

public sealed class RevenuePdfExporter
{
    public byte[] Export(RevenueReportResponse report)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var departmentLabel = report.AppliedFilters.DepartmentId is null
            ? "All"
            : RevenueCsvExporter.Department(report, report.AppliedFilters.DepartmentId);
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(x => x.FontSize(9));
            page.Header().Text("MediCore revenue report").FontSize(19).SemiBold().FontColor(Colors.Blue.Darken2);
            page.Content().PaddingVertical(12).Column(column =>
            {
                column.Spacing(10);
                column.Item().Text($"{report.AppliedFilters.From:yyyy-MM-dd} to {report.AppliedFilters.To:yyyy-MM-dd} (Asia/Colombo)  |  Department: {departmentLabel}  |  Method: {report.AppliedFilters.PaymentMethod ?? "All"}  |  Currency: {report.AppliedFilters.Currency ?? "All"}");
                column.Item().Text("Revenue is based on payments recorded in the period. Currency amounts are never combined.")
                    .FontColor(Colors.Grey.Darken1);
                column.Item().Text("Totals by currency").FontSize(12).SemiBold();
                foreach (var total in report.TotalsByCurrency)
                    column.Item().Text($"{total.Currency} {total.Amount:N2}  |  {total.PaymentCount} payments");
                if (report.TotalsByCurrency.Count == 0)
                    column.Item().Text("No payments in this period.");
                column.Item().Text("By department and payment method").FontSize(12).SemiBold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(4);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                    });
                    table.Header(header =>
                    {
                        foreach (var heading in new[] { "Department", "Method", "Currency", "Amount", "Payments" })
                            header.Cell().Background(Colors.Blue.Lighten4).Padding(5).Text(heading).SemiBold();
                    });
                    foreach (var row in report.Breakdown)
                    {
                        table.Cell().Padding(5).Text(RevenueCsvExporter.Department(report, row.DepartmentId));
                        table.Cell().Padding(5).Text(row.PaymentMethod);
                        table.Cell().Padding(5).Text(row.Currency);
                        table.Cell().Padding(5).Text(row.Amount.ToString("N2", CultureInfo.InvariantCulture));
                        table.Cell().Padding(5).Text(row.PaymentCount.ToString(CultureInfo.InvariantCulture));
                    }
                });
                column.Item().Text("Daily revenue by currency").FontSize(12).SemiBold();
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                    });
                    table.Header(header =>
                    {
                        foreach (var heading in new[] { "Colombo date", "Currency", "Amount", "Payments" })
                            header.Cell().Background(Colors.Blue.Lighten4).Padding(5).Text(heading).SemiBold();
                    });
                    foreach (var row in report.DailyTotals)
                    {
                        table.Cell().Padding(5).Text(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                        table.Cell().Padding(5).Text(row.Currency);
                        table.Cell().Padding(5).Text(row.Amount.ToString("N2", CultureInfo.InvariantCulture));
                        table.Cell().Padding(5).Text(row.PaymentCount.ToString(CultureInfo.InvariantCulture));
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
