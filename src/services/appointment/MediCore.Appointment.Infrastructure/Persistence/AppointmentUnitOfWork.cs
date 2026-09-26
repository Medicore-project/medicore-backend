using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCore.Appointment.Infrastructure.Persistence;

/// <inheritdoc cref="IUnitOfWork"/>
public sealed class AppointmentUnitOfWork : IUnitOfWork
{
    private const string SlotDoctorStartConstraintName = "ux_slots_doctor_start";
    private const string ProcessedMessagePrimaryKeyName = "pk_processed_messages";
    private const string AppointmentSlotConstraintName = "ux_appointments_slot";

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
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: AppointmentSlotConstraintName
            })
        {
            // Someone booked this slot between our availability check and this insert. The slot
            // mutation and the outbox row are discarded with the appointment, which is right —
            // nothing was committed.
            _dbContext.ChangeTracker.Clear();
            throw new SlotAlreadyBookedException(exception);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: ProcessedMessagePrimaryKeyName
            })
        {
            // Another consumer attempt committed this message first; its effect is already saved.
            _dbContext.ChangeTracker.Clear();
            throw new DuplicateProcessedMessageException(exception);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // A slot's xmin no longer matched what we read: another writer committed first. The
            // transaction rolled back, so nothing of ours is saved. Clearing the tracker means a
            // caller that retries re-reads the slot from the database instead of reusing the
            // stale copy.
            _dbContext.ChangeTracker.Clear();
            throw new ConcurrentUpdateException(exception);
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        // Read committed, Postgres's default. Serializable would also close the patient-overlap
        // race, but by aborting whole transactions under load; the explicit patient lock taken
        // inside `work` closes it by waiting instead, and the slot's token covers the slot row.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await work(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (IsTransientConflict(exception))
        {
            // Postgres chose this transaction as the one to abort. Nothing committed; running the
            // whole operation again is the documented remedy.
            _dbContext.ChangeTracker.Clear();
            throw new ConcurrentUpdateException(exception);
        }
        catch
        {
            // Disposing the uncommitted transaction rolls it back. Anything still tracked belongs
            // to that rolled-back attempt and must not leak into a retry.
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    internal static bool IsTransientConflict(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException) is
        {
            SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure
        };
}
