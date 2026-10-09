using MediCore.Billing.Application.DTOs;

namespace MediCore.Billing.Application.Services;

public interface IOutstandingReportQuery
{
    Task<IReadOnlyList<OutstandingSourceRow>> QueryAsync(
        OutstandingQueryCriteria criteria, CancellationToken cancellationToken = default);
}

public interface IOutstandingReportService
{
    Task<OutstandingReportResponse> GenerateAsync(
        OutstandingReportFilter filter, CancellationToken cancellationToken = default);
}

public sealed class OutstandingReportService : IOutstandingReportService
{
    private readonly IOutstandingReportQuery _query;
    private readonly TimeProvider _timeProvider;
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
    private static readonly string[] BucketOrder = ["0-30", "31-60", "61-90", "91+"];

    public OutstandingReportService(IOutstandingReportQuery query, TimeProvider timeProvider)
    {
        _query = query;
        _timeProvider = timeProvider;
    }

    public async Task<OutstandingReportResponse> GenerateAsync(
        OutstandingReportFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var now = _timeProvider.GetUtcNow();
        var asOf = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Colombo).DateTime);
        var rows = await _query.QueryAsync(new OutstandingQueryCriteria(
            filter.DepartmentId, filter.Currency?.ToUpperInvariant(), filter.From, filter.To,
            now.UtcDateTime), cancellationToken);

        var invoices = rows.Select(row =>
        {
            var finalized = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(row.FinalizedAtUtc, DateTimeKind.Utc), Colombo));
            var issued = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(row.IssuedAtUtc, DateTimeKind.Utc), Colombo));
            var age = Math.Max(0, asOf.DayNumber - finalized.DayNumber);
            var bucket = age <= 30 ? BucketOrder[0] : age <= 60 ? BucketOrder[1]
                : age <= 90 ? BucketOrder[2] : BucketOrder[3];
            return new OutstandingInvoiceRow(row.InvoiceId, row.InvoiceNumber, row.DepartmentId,
                row.Currency, issued, finalized, row.Total, row.AmountPaid,
                row.Total - row.AmountPaid, age, bucket);
        }).OrderByDescending(invoice => invoice.AgeDays)
          .ThenBy(invoice => invoice.InvoiceNumber, StringComparer.Ordinal).ToArray();

        return new OutstandingReportResponse(now.UtcDateTime,
            new AppliedOutstandingFilters(filter.DepartmentId, filter.DepartmentName?.Trim(),
                filter.Currency?.ToUpperInvariant(), filter.From, filter.To, asOf),
            invoices.GroupBy(invoice => invoice.Currency)
                .Select(group => new OutstandingCurrencyTotal(group.Key, group.Sum(x => x.BalanceDue), group.Count()))
                .OrderBy(total => total.Currency, StringComparer.Ordinal).ToArray(),
            invoices.GroupBy(invoice => (invoice.DepartmentId, invoice.Currency, invoice.Bucket))
                .Select(group => new OutstandingBucketRow(group.Key.DepartmentId, group.Key.Currency,
                    group.Key.Bucket, group.Sum(x => x.BalanceDue), group.Count()))
                .OrderBy(row => row.Currency, StringComparer.Ordinal)
                .ThenBy(row => row.DepartmentId)
                .ThenBy(row => Array.IndexOf(BucketOrder, row.Bucket)).ToArray(),
            invoices);
    }
}
