namespace MediCore.Billing.Application.DTOs;

public sealed class OutstandingReportFilter
{
    public Guid? DepartmentId { get; init; }
    public string? Currency { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    // A display label only. Billing does not own the department catalogue.
    public string? DepartmentName { get; init; }
}

public enum OutstandingReportFormat { Json, Csv, Pdf }

public sealed record OutstandingQueryCriteria(
    Guid? DepartmentId, string? Currency, DateOnly? From, DateOnly? To, DateTime AsOfUtc);

public sealed record OutstandingSourceRow(
    Guid InvoiceId, string InvoiceNumber, Guid? DepartmentId, string Currency,
    DateTime IssuedAtUtc, DateTime FinalizedAtUtc, decimal Total, decimal AmountPaid);

public sealed record AppliedOutstandingFilters(
    Guid? DepartmentId, string? DepartmentName, string? Currency,
    DateOnly? From, DateOnly? To, DateOnly AsOf);

public sealed record OutstandingCurrencyTotal(string Currency, decimal BalanceDue, int InvoiceCount);

public sealed record OutstandingBucketRow(
    Guid? DepartmentId, string Currency, string Bucket, decimal BalanceDue, int InvoiceCount);

public sealed record OutstandingInvoiceRow(
    Guid InvoiceId, string InvoiceNumber, Guid? DepartmentId, string Currency,
    DateOnly IssuedDate, DateOnly FinalizedDate, decimal Total, decimal AmountPaid,
    decimal BalanceDue, int AgeDays, string Bucket);

public sealed record OutstandingReportResponse(
    DateTime GeneratedAtUtc, AppliedOutstandingFilters AppliedFilters,
    IReadOnlyList<OutstandingCurrencyTotal> TotalsByCurrency,
    IReadOnlyList<OutstandingBucketRow> Buckets,
    IReadOnlyList<OutstandingInvoiceRow> Invoices);
