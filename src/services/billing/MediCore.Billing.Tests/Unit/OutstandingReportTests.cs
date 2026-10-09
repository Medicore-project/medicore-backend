using System.Text;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Application.Validators;
using MediCore.Billing.Infrastructure.Reporting;
using Npgsql;

namespace MediCore.Billing.Tests.Unit;

public sealed class OutstandingReportTests
{
    private static readonly Guid Department = Guid.Parse("dcb90216-5f92-43de-b045-c97646693ee9");
    private static readonly DateOnly AsOf = new(2026, 10, 9);
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Buckets_use_payable_date_and_remaining_balance_without_mixing_currencies()
    {
        var query = new RecordingQuery([
            Row(0, "LKR", 100m, 30m), Row(30, "LKR", 50m),
            Row(31, "LKR", 40m), Row(60, "LKR", 30m),
            Row(61, "LKR", 20m), Row(90, "LKR", 10m),
            Row(91, "LKR", 5m), Row(91, "USD", 7m)
        ]);
        var report = await new OutstandingReportService(query, new FixedClock(Now))
            .GenerateAsync(new OutstandingReportFilter());

        Assert.Equal(225m, Assert.Single(report.TotalsByCurrency, x => x.Currency == "LKR").BalanceDue);
        Assert.Equal(7m, Assert.Single(report.TotalsByCurrency, x => x.Currency == "USD").BalanceDue);
        Assert.Equal(120m, Assert.Single(report.Buckets, x => x.Currency == "LKR" && x.Bucket == "0-30").BalanceDue);
        Assert.Equal(70m, Assert.Single(report.Buckets, x => x.Currency == "LKR" && x.Bucket == "31-60").BalanceDue);
        Assert.Equal(30m, Assert.Single(report.Buckets, x => x.Currency == "LKR" && x.Bucket == "61-90").BalanceDue);
        Assert.Equal(5m, Assert.Single(report.Buckets, x => x.Currency == "LKR" && x.Bucket == "91+").BalanceDue);
        Assert.Equal(AsOf, report.AppliedFilters.AsOf);
        Assert.Null(query.Criteria!.From);
        Assert.Null(query.Criteria.To);
        Assert.Equal(Now.UtcDateTime, query.Criteria.AsOfUtc);
    }

    [Fact]
    public async Task Filters_are_forwarded_without_a_default_month()
    {
        var query = new RecordingQuery([]);
        var filter = new OutstandingReportFilter
        {
            DepartmentId = Department, DepartmentName = " Cardiology ", Currency = "lkr",
            From = new DateOnly(2026, 9, 1), To = new DateOnly(2026, 10, 9)
        };
        var report = await new OutstandingReportService(query, new FixedClock(Now)).GenerateAsync(filter);

        Assert.Equal(Department, query.Criteria!.DepartmentId);
        Assert.Equal("LKR", query.Criteria.Currency);
        Assert.Equal(filter.From, query.Criteria.From);
        Assert.Equal(filter.To, query.Criteria.To);
        Assert.Equal("Cardiology", report.AppliedFilters.DepartmentName);
    }

    [Fact]
    public void Validates_issue_date_range_and_currency()
    {
        var result = new OutstandingReportFilterValidator().Validate(new OutstandingReportFilter
        {
            DepartmentId = Guid.Empty, Currency = "LKR1",
            From = new DateOnly(2026, 1, 1), To = new DateOnly(2027, 1, 2)
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(OutstandingReportFilter.DepartmentId));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(OutstandingReportFilter.Currency));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(OutstandingReportFilter.To));
    }

    [Fact]
    public void Raw_sql_excludes_non_payable_invoices_and_uses_colombo_date_parameters()
    {
        using var connection = new NpgsqlConnection();
        using var command = OutstandingReportQuery.BuildCommand(connection,
            new OutstandingQueryCriteria(Department, "LKR", new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 9), Now.UtcDateTime));

        Assert.Contains("i.\"Status\" = @status", command.CommandText);
        Assert.Contains("i.\"Total\" > i.\"AmountPaid\"", command.CommandText);
        Assert.Contains("i.\"FinalizedAtUtc\" <= @asOfUtc", command.CommandText);
        Assert.DoesNotContain(Department.ToString(), command.CommandText);
        Assert.Equal("Payable", command.Parameters["status"].Value);
        Assert.Equal(new DateTime(2026, 9, 30, 18, 30, 0, DateTimeKind.Utc), command.Parameters["fromUtc"].Value);
        Assert.Equal(new DateTime(2026, 10, 9, 18, 30, 0, DateTimeKind.Utc), command.Parameters["toUtc"].Value);
    }

    [Fact]
    public void Exports_filtered_ageing_and_invoice_details_safely()
    {
        var report = new OutstandingReportResponse(Now.UtcDateTime,
            new AppliedOutstandingFilters(Department, "=Cardiology", "LKR", null, null, AsOf),
            [new("LKR", 70m, 1)], [new(Department, "LKR", "31-60", 70m, 1)],
            [new(Guid.NewGuid(), "INV-001", Department, "LKR", AsOf.AddDays(-32),
                AsOf.AddDays(-31), 100m, 30m, 70m, 31, "31-60")]);

        var csv = Encoding.UTF8.GetString(new OutstandingCsvExporter().Export(report));
        Assert.Contains("'=Cardiology", csv);
        Assert.Contains("INV-001", csv);
        Assert.Contains("31-60", csv);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(new OutstandingPdfExporter().Export(report)[..4]));
    }

    private static OutstandingSourceRow Row(int age, string currency, decimal total, decimal paid = 0m)
    {
        var date = AsOf.AddDays(-age);
        var finalized = date.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Utc);
        return new OutstandingSourceRow(Guid.NewGuid(), $"INV-{currency}-{age}", Department,
            currency, finalized.AddDays(-1), finalized, total, paid);
    }

    private sealed class RecordingQuery(IReadOnlyList<OutstandingSourceRow> rows) : IOutstandingReportQuery
    {
        public OutstandingQueryCriteria? Criteria { get; private set; }
        public Task<IReadOnlyList<OutstandingSourceRow>> QueryAsync(
            OutstandingQueryCriteria criteria, CancellationToken cancellationToken = default)
        {
            Criteria = criteria;
            return Task.FromResult(rows);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
