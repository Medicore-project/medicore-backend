using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Appointment.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IAppointmentHistoryRepository"/>
public sealed class AppointmentHistoryRepository : IAppointmentHistoryRepository
{
    private readonly AppointmentDbContext _dbContext;

    public AppointmentHistoryRepository(AppointmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(AppointmentHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        return _dbContext.AppointmentHistory.AddAsync(entry, cancellationToken).AsTask();
    }

    public async Task<IReadOnlyList<AppointmentHistoryEntry>> ListForAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        // Served by ix_appointment_history_appointment_occurred.
        return await _dbContext.AppointmentHistory
            .AsNoTracking()
            .Where(entry => entry.AppointmentId == appointmentId)
            .OrderBy(entry => entry.OccurredAtUtc)
            .ToListAsync(cancellationToken);
    }
}
