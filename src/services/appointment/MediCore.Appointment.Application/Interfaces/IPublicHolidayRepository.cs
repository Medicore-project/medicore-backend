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
}
