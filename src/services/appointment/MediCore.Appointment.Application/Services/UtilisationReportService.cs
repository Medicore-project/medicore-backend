using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IUtilisationReportService"/>
/// <remarks>
/// The query does the counting in SQL; this class only resolves the period and does the arithmetic,
/// so every rule about what a rate means is unit-testable without a database.
/// </remarks>
public sealed class UtilisationReportService : IUtilisationReportService
{
    private readonly IUtilisationReportQuery _query;
    private readonly TimeProvider _timeProvider;

    public UtilisationReportService(IUtilisationReportQuery query, TimeProvider timeProvider)
    {
        _query = query;
        _timeProvider = timeProvider;
    }

    public async Task<UtilisationReportResponse> GenerateAsync(
        UtilisationReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var (from, to) = ResolvePeriod(filter, ColomboTime.Today(_timeProvider));
        if (from > to)
        {
            throw new ArgumentException("To date must be on or after from date.", nameof(filter));
        }

        var rows = await _query.QueryAsync(
            new UtilisationQueryCriteria(filter.DoctorId, filter.DepartmentId, from, to),
            cancellationToken);

        var doctors = rows
            .Select(ToDoctorRow)
            .OrderBy(row => row.DoctorName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.DoctorId)
            .ToArray();

        return new UtilisationReportResponse(
            GeneratedAtUtc: _timeProvider.GetUtcNow().UtcDateTime,
            AppliedFilters: new AppliedUtilisationFilters(
                filter.DoctorId,
                filter.DepartmentId,
                // A label with no department behind it would claim a filter that was not applied.
                filter.DepartmentId is null || string.IsNullOrWhiteSpace(filter.DepartmentName)
                    ? null
                    : filter.DepartmentName.Trim(),
                from,
                to,
                IsDefaultPeriod: filter.From is null && filter.To is null),
            Totals: ToTotals(doctors),
            Doctors: doctors);
    }

    /// <summary>
    /// SCRUM-38 AC1: no dates means the current month in Colombo, where the clinic's dates live. A
    /// single date anchors a month too — from it to that month's end, or from that month's start to
    /// it — so a half-given range still reads as "a month".
    /// </summary>
    private static (DateOnly From, DateOnly To) ResolvePeriod(UtilisationReportFilter filter, DateOnly today) =>
        (filter.From, filter.To) switch
        {
            ({ } from, { } to) => (from, to),
            ({ } from, null) => (from, EndOfMonth(from)),
            (null, { } to) => (StartOfMonth(to), to),
            _ => (StartOfMonth(today), EndOfMonth(today))
        };

    /// <summary>
    /// A fraction to four places, or null when there is nothing to divide by — "no data", which a
    /// rate of 0 would misreport as "never".
    /// </summary>
    private static decimal? Rate(int numerator, int denominator) =>
        denominator == 0
            ? null
            : Math.Round((decimal)numerator / denominator, 4, MidpointRounding.AwayFromZero);

    private static DoctorUtilisationRow ToDoctorRow(UtilisationSourceRow row) => new(
        row.DoctorId,
        row.FullName,
        row.Specialization,
        row.DepartmentId,
        row.IsActive,
        row.Completed,
        row.NoShow,
        row.Cancelled,
        row.Booked,
        Total: row.Completed + row.NoShow + row.Booked,
        NoShowRate: Rate(row.NoShow, row.Completed + row.NoShow),
        row.BookableSlots,
        row.UsedSlots,
        FillRate: Rate(row.UsedSlots, row.BookableSlots));

    private static UtilisationTotals ToTotals(IReadOnlyCollection<DoctorUtilisationRow> doctors)
    {
        var completed = doctors.Sum(d => d.Completed);
        var noShow = doctors.Sum(d => d.NoShow);
        var booked = doctors.Sum(d => d.Booked);
        var bookableSlots = doctors.Sum(d => d.BookableSlots);
        var usedSlots = doctors.Sum(d => d.UsedSlots);

        return new UtilisationTotals(
            Doctors: doctors.Count,
            Completed: completed,
            NoShow: noShow,
            Cancelled: doctors.Sum(d => d.Cancelled),
            Booked: booked,
            Total: completed + noShow + booked,
            NoShowRate: Rate(noShow, completed + noShow),
            BookableSlots: bookableSlots,
            UsedSlots: usedSlots,
            FillRate: Rate(usedSlots, bookableSlots));
    }

    private static DateOnly StartOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static DateOnly EndOfMonth(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}
