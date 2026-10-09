using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Entities;

namespace MediCore.Billing.Application.Services;

public interface IRevenueReportQuery
{
    Task<IReadOnlyList<RevenueSourceRow>> QueryAsync(
        RevenueQueryCriteria criteria, CancellationToken cancellationToken = default);
}

public interface IRevenueReportService
{
    Task<RevenueReportResponse> GenerateAsync(
        RevenueReportFilter filter, CancellationToken cancellationToken = default);
}

public sealed class RevenueReportService : IRevenueReportService
{
    private readonly IRevenueReportQuery _query;
    private readonly TimeProvider _timeProvider;
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");

    public RevenueReportService(IRevenueReportQuery query, TimeProvider timeProvider)
    {
        _query = query;
        _timeProvider = timeProvider;
    }

    public async Task<RevenueReportResponse> GenerateAsync(
        RevenueReportFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var now = _timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Colombo).DateTime);
        var (from, to) = (filter.From, filter.To) switch
        {
            ({ } first, { } last) => (first, last),
            ({ } first, null) => (first, EndOfMonth(first)),
            (null, { } last) => (new DateOnly(last.Year, last.Month, 1), last),
            _ => (new DateOnly(today.Year, today.Month, 1), EndOfMonth(today))
        };
        if (from > to || to == DateOnly.MaxValue || to.DayNumber - from.DayNumber + 1 > 366)
            throw new ArgumentException("Invalid revenue report period.", nameof(filter));

        var rows = await _query.QueryAsync(new RevenueQueryCriteria(
            filter.DepartmentId, PaymentMethods.Normalize(filter.PaymentMethod), filter.Currency?.ToUpperInvariant(), from, to),
            cancellationToken);

        return new RevenueReportResponse(
            now.UtcDateTime,
            new AppliedRevenueFilters(filter.DepartmentId,
                filter.DepartmentId is null ? null : filter.DepartmentName?.Trim(),
                PaymentMethods.Normalize(filter.PaymentMethod), filter.Currency?.ToUpperInvariant(), from, to,
                filter.From is null && filter.To is null),
            rows.GroupBy(x => x.Currency)
                .Select(g => new RevenueCurrencyTotal(g.Key, g.Sum(x => x.Amount), g.Sum(x => x.PaymentCount)))
                .OrderBy(x => x.Currency).ToArray(),
            rows.GroupBy(x => (x.DepartmentId, x.PaymentMethod, x.Currency))
                .Select(g => new RevenueBreakdownRow(g.Key.DepartmentId, g.Key.PaymentMethod,
                    g.Key.Currency, g.Sum(x => x.Amount), g.Sum(x => x.PaymentCount)))
                .OrderBy(x => x.Currency).ThenBy(x => x.DepartmentId).ThenBy(x => x.PaymentMethod).ToArray(),
            rows.GroupBy(x => (x.Date, x.Currency))
                .Select(g => new RevenueDailyTotal(g.Key.Date, g.Key.Currency,
                    g.Sum(x => x.Amount), g.Sum(x => x.PaymentCount)))
                .OrderBy(x => x.Date).ThenBy(x => x.Currency).ToArray());
    }

    private static DateOnly EndOfMonth(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}
