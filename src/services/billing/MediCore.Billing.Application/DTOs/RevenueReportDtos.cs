namespace MediCore.Billing.Application.DTOs;

public sealed class RevenueReportFilter
{
    public Guid? DepartmentId { get; init; }
    public string? PaymentMethod { get; init; }
    public string? Currency { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    // Display label only; never used by SQL. Billing does not own the department catalogue.
    public string? DepartmentName { get; init; }
}

public enum RevenueReportFormat { Json, Csv, Pdf }

public sealed record RevenueQueryCriteria(
    Guid? DepartmentId, string? PaymentMethod, string? Currency,
    DateOnly From, DateOnly To);

public sealed record RevenueSourceRow(
    DateOnly Date, Guid? DepartmentId, string PaymentMethod,
    string Currency, decimal Amount, long PaymentCount);

public sealed record AppliedRevenueFilters(
    Guid? DepartmentId, string? DepartmentName, string? PaymentMethod,
    string? Currency, DateOnly From, DateOnly To, bool IsDefaultPeriod);

public sealed record RevenueCurrencyTotal(string Currency, decimal Amount, long PaymentCount);

public sealed record RevenueBreakdownRow(
    Guid? DepartmentId, string PaymentMethod, string Currency,
    decimal Amount, long PaymentCount);

public sealed record RevenueDailyTotal(DateOnly Date, string Currency, decimal Amount, long PaymentCount);

public sealed record RevenueReportResponse(
    DateTime GeneratedAtUtc,
    AppliedRevenueFilters AppliedFilters,
    IReadOnlyList<RevenueCurrencyTotal> TotalsByCurrency,
    IReadOnlyList<RevenueBreakdownRow> Breakdown,
    IReadOnlyList<RevenueDailyTotal> DailyTotals);
