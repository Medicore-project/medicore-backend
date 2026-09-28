namespace MediCore.Appointment.Application.DTOs;

// ── Request ───────────────────────────────────────────────────────────────────

/// <summary>
/// The doctor utilisation report's filters (SCRUM-38), bound from the query string. Every one is
/// optional and they combine freely.
/// </summary>
/// <remarks>
/// A class with init properties rather than a positional record, as the demographics report's filter
/// is, so <c>[FromQuery]</c> binds it without a constructor.
/// </remarks>
public sealed class UtilisationReportFilter
{
    /// <summary>One doctor's staff id. Omitted: every doctor.</summary>
    public Guid? DoctorId { get; init; }

    /// <summary>Identity's department id. Omitted: every department.</summary>
    public Guid? DepartmentId { get; init; }

    /// <summary>First Colombo date of the period, inclusive.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Last Colombo date of the period, inclusive.</summary>
    public DateOnly? To { get; init; }

    /// <summary>
    /// The selected department's name, for the export header only. Display text supplied by the
    /// page, which can resolve it from Identity; this service's doctor cache holds only the id. It is
    /// never part of the query — see <see cref="UtilisationQueryCriteria"/> — and is ignored unless
    /// <see cref="DepartmentId"/> is set.
    /// </summary>
    public string? DepartmentName { get; init; }
}

/// <summary>The response format of <c>GET reports/utilisation</c>.</summary>
public enum UtilisationReportFormat
{
    Json,
    Csv,
    Pdf
}

// ── Between the service and the query ─────────────────────────────────────────

/// <summary>
/// The filters as the query receives them: the period already resolved to concrete dates, and no
/// display text, so nothing but ids and dates can reach the SQL.
/// </summary>
public sealed record UtilisationQueryCriteria(
    Guid? DoctorId,
    Guid? DepartmentId,
    DateOnly From,
    DateOnly To);

/// <summary>One doctor's raw counts for the period, as the query reads them.</summary>
/// <param name="Booked">Appointments still <c>Booked</c>: upcoming, or past and never resolved.</param>
/// <param name="BookableSlots">Slots in the period that are not <c>Blocked</c>.</param>
/// <param name="UsedSlots">Slots <c>Booked</c>, <c>Offered</c> or <c>Flagged</c> — taken by someone.</param>
public sealed record UtilisationSourceRow(
    Guid DoctorId,
    string FullName,
    string Specialization,
    Guid DepartmentId,
    bool IsActive,
    int Completed,
    int NoShow,
    int Cancelled,
    int Booked,
    int BookableSlots,
    int UsedSlots);

// ── Response ──────────────────────────────────────────────────────────────────

/// <summary>The filters the report was actually produced with.</summary>
/// <param name="From">The resolved first date — the current month's first day when none was given.</param>
/// <param name="To">The resolved last date.</param>
/// <param name="IsDefaultPeriod">True when neither date was given and the current month was used.</param>
public sealed record AppliedUtilisationFilters(
    Guid? DoctorId,
    Guid? DepartmentId,
    string? DepartmentName,
    DateOnly From,
    DateOnly To,
    bool IsDefaultPeriod);

/// <summary>One doctor's line in the report.</summary>
/// <param name="Total">Every appointment that was not cancelled: completed, no-show and still booked.</param>
/// <param name="NoShowRate">
/// No-shows over attended outcomes (completed + no-show), 0–1 to four places. Still-booked
/// appointments are left out so future bookings do not dilute it mid-month. Null when there is no
/// outcome to divide by.
/// </param>
/// <param name="FillRate">
/// Used slots over bookable slots, 0–1 to four places. What "over-subscribed" means here. Null when
/// the doctor had no bookable slots.
/// </param>
public sealed record DoctorUtilisationRow(
    Guid DoctorId,
    string DoctorName,
    string Specialization,
    Guid DepartmentId,
    bool IsActive,
    int Completed,
    int NoShow,
    int Cancelled,
    int Booked,
    int Total,
    decimal? NoShowRate,
    int BookableSlots,
    int UsedSlots,
    decimal? FillRate);

/// <summary>
/// Every doctor in the report together. The rates come from the summed counts, not an average of
/// each doctor's rate, so a doctor with two appointments does not weigh as much as one with two
/// hundred.
/// </summary>
public sealed record UtilisationTotals(
    int Doctors,
    int Completed,
    int NoShow,
    int Cancelled,
    int Booked,
    int Total,
    decimal? NoShowRate,
    int BookableSlots,
    int UsedSlots,
    decimal? FillRate);

/// <summary>The doctor utilisation report, as JSON and as the source of both exports.</summary>
public sealed record UtilisationReportResponse(
    DateTime GeneratedAtUtc,
    AppliedUtilisationFilters AppliedFilters,
    UtilisationTotals Totals,
    IReadOnlyList<DoctorUtilisationRow> Doctors);
