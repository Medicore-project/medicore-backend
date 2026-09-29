using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Interfaces;

/// <summary>Data-access contract for <see cref="PublicHoliday"/>.</summary>
public interface IPublicHolidayRepository
{
    /// <summary>
    /// Holidays falling in <paramref name="from"/>..<paramref name="to"/>, both inclusive.
    /// Soft-deleted rows are excluded by the global query filter.
    /// </summary>
    Task<IReadOnlyList<PublicHoliday>> GetBetweenAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>Every holiday on record, earliest first.</summary>
    Task<IReadOnlyList<PublicHoliday>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a holiday is already declared on <paramref name="date"/>. Backs a clear 409 instead
    /// of letting the partial unique index <c>ux_public_holidays_date</c> surface as a 500.
    /// </summary>
    Task<bool> ExistsOnDateAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Returns a change-tracked holiday by its business key, for withdrawal.</summary>
    Task<PublicHoliday?> GetTrackedByHolidayIdAsync(
        Guid holidayId,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new holiday for insertion (not yet committed).</summary>
    Task AddAsync(PublicHoliday holiday, CancellationToken cancellationToken = default);
}
