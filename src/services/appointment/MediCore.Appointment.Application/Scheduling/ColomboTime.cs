namespace MediCore.Appointment.Application.Scheduling;

/// <summary>
/// Conversions between Asia/Colombo wall-clock time (how schedules are written) and UTC instants
/// (how slots are stored).
/// </summary>
/// <remarks>
/// A fixed offset is used rather than <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/>
/// on purpose. The timezone identifier differs by platform — "Sri Lanka Standard Time" on Windows,
/// "Asia/Colombo" on Linux — and resolving either requires ICU / tzdata to be present in the
/// container. Development runs on Windows while CI runs on Linux, so the lookup is a portability
/// hazard for no benefit: Sri Lanka has observed a constant UTC+05:30 with no daylight saving
/// since 2006.
/// </remarks>
public static class ColomboTime
{
    /// <summary>Asia/Colombo's fixed offset from UTC: +05:30.</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromMinutes(330);

    /// <summary>
    /// Converts a Colombo calendar date and wall-clock time into the UTC instant it denotes.
    /// The result is always <see cref="DateTimeKind.Utc"/>, which Npgsql requires for
    /// <c>timestamp with time zone</c> columns.
    /// </summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time) =>
        DateTime.SpecifyKind(date.ToDateTime(time) - Offset, DateTimeKind.Utc);

    /// <summary>Converts a UTC instant into the Colombo wall-clock time it corresponds to.</summary>
    public static DateTime ToColombo(DateTime utcInstant) =>
        DateTime.SpecifyKind(utcInstant.Add(Offset), DateTimeKind.Unspecified);

    /// <summary>Converts a UTC instant into the Colombo calendar date it falls on.</summary>
    public static DateOnly ToColomboDate(DateTime utcInstant) =>
        DateOnly.FromDateTime(ToColombo(utcInstant));

    /// <summary>Today's date in Colombo, according to the supplied clock.</summary>
    public static DateOnly Today(TimeProvider timeProvider) =>
        ToColomboDate(timeProvider.GetUtcNow().UtcDateTime);
}
