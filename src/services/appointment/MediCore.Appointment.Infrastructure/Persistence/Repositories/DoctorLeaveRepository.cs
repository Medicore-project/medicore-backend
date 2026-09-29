using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Appointment.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDoctorLeaveRepository"/>. The global
/// <c>HasQueryFilter(!IsDeleted)</c> excludes soft-deleted rows from every query here.
/// </summary>
public sealed class DoctorLeaveRepository : IDoctorLeaveRepository
{
    private readonly AppointmentDbContext _dbContext;

    public DoctorLeaveRepository(AppointmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<DoctorLeave>> GetApprovedForDoctorBetweenAsync(
        Guid doctorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default) =>
        await _dbContext.DoctorLeaves
            .AsNoTracking()
            .Where(leave =>
                leave.DoctorId == doctorId
                && leave.Status == LeaveStatus.Approved
                // Inclusive range intersection: the leave period overlaps the window at all.
                && leave.StartDate <= to
                && leave.EndDate >= from)
            .OrderBy(leave => leave.StartDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DoctorLeave>> GetAllForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.DoctorLeaves
            .AsNoTracking()
            .Where(leave => leave.DoctorId == doctorId)
            .OrderByDescending(leave => leave.StartDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DoctorLeave>> GetPendingAsync(
        CancellationToken cancellationToken = default) =>
        await _dbContext.DoctorLeaves
            .AsNoTracking()
            .Where(leave => leave.Status == LeaveStatus.Pending)
            .OrderBy(leave => leave.StartDate)
            .ToListAsync(cancellationToken);

    public Task<DoctorLeave?> GetTrackedByLeaveIdAsync(
        Guid leaveId,
        CancellationToken cancellationToken = default) =>
        _dbContext.DoctorLeaves.SingleOrDefaultAsync(leave => leave.LeaveId == leaveId, cancellationToken);

    public Task AddAsync(DoctorLeave leave, CancellationToken cancellationToken = default) =>
        _dbContext.DoctorLeaves.AddAsync(leave, cancellationToken).AsTask();
}
