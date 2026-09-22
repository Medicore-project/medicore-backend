using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Appointment.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDoctorCacheRepository"/>. The global
/// <c>HasQueryFilter(!IsDeleted)</c> excludes soft-deleted rows from every query here.
/// </summary>
public sealed class DoctorCacheRepository : IDoctorCacheRepository
{
    private readonly AppointmentDbContext _dbContext;

    public DoctorCacheRepository(AppointmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<DoctorCache?> GetTrackedByDoctorIdAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default) =>
        _dbContext.DoctorCaches
            .FirstOrDefaultAsync(doctor => doctor.DoctorId == doctorId, cancellationToken);

    public Task<DoctorCache?> GetActiveAsync(Guid doctorId, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorCaches
            .AsNoTracking()
            .FirstOrDefaultAsync(doctor => doctor.DoctorId == doctorId && doctor.IsActive, cancellationToken);

    public async Task<IReadOnlyList<DoctorCache>> ListActiveAsync(
        string? specialization,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.DoctorCaches
            .AsNoTracking()
            .Where(doctor => doctor.IsActive);

        if (!string.IsNullOrWhiteSpace(specialization))
        {
            var wanted = specialization.Trim().ToLower();
            query = query.Where(doctor => doctor.Specialization.ToLower() == wanted);
        }

        return await query
            .OrderBy(doctor => doctor.FullName)
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(DoctorCache doctor, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorCaches.AddAsync(doctor, cancellationToken).AsTask();
}
