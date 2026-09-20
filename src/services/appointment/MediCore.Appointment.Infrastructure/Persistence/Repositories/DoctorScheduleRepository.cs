using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Appointment.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDoctorScheduleRepository"/>. The global
/// <c>HasQueryFilter(!IsDeleted)</c> excludes soft-deleted rows from every query here.
/// </summary>
public sealed class DoctorScheduleRepository : IDoctorScheduleRepository
{
    private readonly AppointmentDbContext _dbContext;

    public DoctorScheduleRepository(AppointmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<DoctorSchedule>> GetActiveForDoctorAsync(
        Guid doctorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default) =>
        await _dbContext.DoctorSchedules
            .AsNoTracking()
            .Where(schedule =>
                schedule.DoctorId == doctorId
                && schedule.IsActive
                // Effective range intersects the window. A null EffectiveTo is open-ended, so it
                // can only ever extend past the window's end.
                && schedule.EffectiveFrom <= to
                && (schedule.EffectiveTo == null || schedule.EffectiveTo >= from))
            .OrderBy(schedule => schedule.DayOfWeek)
            .ThenBy(schedule => schedule.StartTime)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DoctorSchedule>> GetForOverlapCheckAsync(
        Guid doctorId,
        DayOfWeek dayOfWeek,
        CancellationToken cancellationToken = default) =>
        await _dbContext.DoctorSchedules
            .AsNoTracking()
            // Paused schedules are deliberately included — see IDoctorScheduleRepository.
            .Where(schedule => schedule.DoctorId == doctorId && schedule.DayOfWeek == dayOfWeek)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetDoctorIdsWithActiveSchedulesAsync(
        CancellationToken cancellationToken = default) =>
        await _dbContext.DoctorSchedules
            .AsNoTracking()
            .Where(schedule => schedule.IsActive)
            .Select(schedule => schedule.DoctorId)
            .Distinct()
            .ToListAsync(cancellationToken);
}
