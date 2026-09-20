using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCore.Appointment.Infrastructure.Persistence;

/// <inheritdoc cref="IUnitOfWork"/>
public sealed class AppointmentUnitOfWork : IUnitOfWork
{
    private const string SlotDoctorStartConstraintName = "ux_slots_doctor_start";

    private readonly AppointmentDbContext _dbContext;

    public AppointmentUnitOfWork(AppointmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: SlotDoctorStartConstraintName
            })
        {
            // The failed entities stay in the change tracker otherwise, so a later save on the same
            // scoped context would retry them and fail again.
            _dbContext.ChangeTracker.Clear();
            throw new DuplicateSlotException(exception);
        }
    }
}
