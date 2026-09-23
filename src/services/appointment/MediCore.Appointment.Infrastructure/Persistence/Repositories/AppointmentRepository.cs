using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IAppointmentRepository"/>
public sealed class AppointmentRepository : IAppointmentRepository
{
    private readonly AppointmentDbContext _dbContext;

    public AppointmentRepository(AppointmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(AppointmentEntity appointment, CancellationToken cancellationToken = default)
    {
        return _dbContext.Appointments.AddAsync(appointment, cancellationToken).AsTask();
    }

    public Task<AppointmentEntity?> FindPatientOverlapAsync(
        Guid patientId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        // Half-open intervals: strict comparisons on both sides, so an appointment ending exactly
        // when this one starts is not a clash. See the interface for why that matters.
        // Soft-deleted rows are excluded by the global query filter.
        return _dbContext.Appointments
            .AsNoTracking()
            .Where(appointment =>
                appointment.PatientId == patientId
                && appointment.Status == AppointmentStatus.Booked
                && appointment.StartUtc < endUtc
                && appointment.EndUtc > startUtc)
            .OrderBy(appointment => appointment.StartUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<AppointmentEntity?> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Appointments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                appointment => appointment.AppointmentId == appointmentId,
                cancellationToken);
    }
}
