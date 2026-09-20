using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Appointment.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPublicHolidayRepository"/>. The global
/// <c>HasQueryFilter(!IsDeleted)</c> excludes soft-deleted rows from every query here.
/// </summary>
public sealed class PublicHolidayRepository : IPublicHolidayRepository
{
    private readonly AppointmentDbContext _dbContext;

    public PublicHolidayRepository(AppointmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PublicHoliday>> GetBetweenAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default) =>
        await _dbContext.PublicHolidays
            .AsNoTracking()
            .Where(holiday => holiday.Date >= from && holiday.Date <= to)
            .OrderBy(holiday => holiday.Date)
            .ToListAsync(cancellationToken);
}
