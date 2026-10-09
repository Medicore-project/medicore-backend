using System.Text;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Application.Validators;
using MediCore.Billing.Infrastructure.Reporting;
using Npgsql;

namespace MediCore.Billing.Tests.Unit;

public sealed class RevenueReportTests
{
    private static readonly Guid Department = Guid.Parse("dcb90216-5f92-43de-b045-c97646693ee9");

    [Fact]
    public async Task Summarises_payment_rows_without_mixing_currencies()
    {
        var query = new RecordingQuery([
            new(new DateOnly(2026, 10, 1), Department, "Cash", "LKR", 100m, 1),
            new(new DateOnly(2026, 10, 2), Department, "Cash", "LKR", 50m, 1),
            new(new DateOnly(2026, 10, 2), null, "Card", "LKR", 25m, 1),
            new(new DateOnly(2026, 10, 2), Department, "Card", "USD", 5m, 1)
        ]);
        var service = new RevenueReportService(query, TimeProvider.System);

        var report = await service.GenerateAsync(new RevenueReportFilter
        {
            From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 2)
        });

        Assert.Equal(175m, Assert.Single(report.TotalsByCurrency, x => x.Currency == "LKR").Amount);
        Assert.Equal(5m, Assert.Single(report.TotalsByCurrency, x => x.Currency == "USD").Amount);
        Assert.Equal(150m, Assert.Single(report.Breakdown,
            x => x.DepartmentId == Department && x.PaymentMethod == "Cash").Amount);
        Assert.Equal(75m, Assert.Single(report.DailyTotals,
            x => x.Date == new DateOnly(2026, 10, 2) && x.Currency == "LKR").Amount);
        Assert.Equal(new DateOnly(2026, 10, 1), query.Criteria!.From);
    }

    [Fact]
    public async Task Normalises_filters_before_query_and_uses_month_for_single_date()
    {
        var query = new RecordingQuery([]);
        var service = new RevenueReportService(query, TimeProvider.System);
        var report = await service.GenerateAsync(new RevenueReportFilter
        {
            DepartmentId = Department, DepartmentName = " Cardiology ",
            PaymentMethod = "cash", Currency = "lkr", From = new DateOnly(2026, 10, 6)
        });

        Assert.Equal(new DateOnly(2026, 10, 31), query.Criteria!.To);
        Assert.Equal("Cash", query.Criteria.PaymentMethod);
        Assert.Equal("LKR", query.Criteria.Currency);
        Assert.Equal("Cardiology", report.AppliedFilters.DepartmentName);
    }

    [Fact]
    public async Task Default_month_follows_colombo_not_utc()
    {
        var query = new RecordingQuery([]);
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 30, 19, 0, 0, TimeSpan.Zero));
        var report = await new RevenueReportService(query, clock).GenerateAsync(new RevenueReportFilter());

        Assert.Equal(new DateOnly(2026, 10, 1), query.Criteria!.From);
        Assert.Equal(new DateOnly(2026, 10, 31), query.Criteria.To);
        Assert.True(report.AppliedFilters.IsDefaultPeriod);
    }

    [Fact]
    public void Validates_range_method_and_currency()
    {
        var validator = new RevenueReportFilterValidator();
        var result = validator.Validate(new RevenueReportFilter
        {
            DepartmentId = Guid.Empty, PaymentMethod = "Cheque", Currency = "LKR1",
            From = new DateOnly(2026, 1, 1), To = new DateOnly(2027, 1, 2)
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(RevenueReportFilter.DepartmentId));
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(RevenueReportFilter.PaymentMethod));
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(RevenueReportFilter.Currency));
    }

    [Fact]
    public void Raw_sql_uses_typed_parameters_and_inclusive_colombo_dates()
    {
        using var connection = new NpgsqlConnection();
        using var command = RevenueReportQuery.BuildCommand(connection,
            new RevenueQueryCriteria(Department, "Cash", "LKR",
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)));

        Assert.Contains("FROM medicore_billing.payments", command.CommandText);
        Assert.Contains("SUM(p.\"Amount\")", command.CommandText);
        Assert.Contains("i.\"DepartmentId\" = @departmentId", command.CommandText);
        Assert.DoesNotContain(Department.ToString(), command.CommandText);
        Assert.Equal(new DateTime(2026, 9, 30, 18, 30, 0, DateTimeKind.Utc), command.Parameters["fromUtc"].Value);
        Assert.Equal(new DateTime(2026, 10, 2, 18, 30, 0, DateTimeKind.Utc), command.Parameters["toUtc"].Value);
    }

    [Fact]
    public void Raw_sql_never_embeds_filter_text()
    {
        using var connection = new NpgsqlConnection();
        using var command = RevenueReportQuery.BuildCommand(connection,
            new RevenueQueryCriteria(null, "Cash' OR true --", null,
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)));

        Assert.DoesNotContain("Cash' OR true --", command.CommandText);
        Assert.Equal("Cash' OR true --", command.Parameters["method"].Value);
    }

    [Fact]
    public void Exports_csv_safely_and_pdf()
    {
        var report = new RevenueReportResponse(DateTime.UtcNow,
            new AppliedRevenueFilters(Department, "=Cardiology", null, null,
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), false),
            [new("LKR", 100m, 1)], [new(Department, "Cash", "LKR", 100m, 1)],
            [new(new DateOnly(2026, 10, 1), "LKR", 100m, 1)]);

        var csv = Encoding.UTF8.GetString(new RevenueCsvExporter().Export(report));
        Assert.Contains("'=Cardiology", csv);
        Assert.Contains("2026-10-01,LKR,100,1", csv);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(new RevenuePdfExporter().Export(report)[..4]));
    }

    private sealed class RecordingQuery(IReadOnlyList<RevenueSourceRow> rows) : IRevenueReportQuery
    {
        public RevenueQueryCriteria? Criteria { get; private set; }
        public Task<IReadOnlyList<RevenueSourceRow>> QueryAsync(
            RevenueQueryCriteria criteria, CancellationToken cancellationToken = default)
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
