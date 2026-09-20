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

    public async Task<IReadOnlyList<PublicHoliday>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        await _dbContext.PublicHolidays
            .AsNoTracking()
            .OrderBy(holiday => holiday.Date)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsOnDateAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        _dbContext.PublicHolidays.AnyAsync(holiday => holiday.Date == date, cancellationToken);

    public Task<PublicHoliday?> GetTrackedByHolidayIdAsync(
        Guid holidayId,
        CancellationToken cancellationToken = default) =>
        _dbContext.PublicHolidays
            .SingleOrDefaultAsync(holiday => holiday.HolidayId == holidayId, cancellationToken);

    public Task AddAsync(PublicHoliday holiday, CancellationToken cancellationToken = default) =>
        _dbContext.PublicHolidays.AddAsync(holiday, cancellationToken).AsTask();
}
