using MediCore.Billing.Application.Exceptions;
using MediCore.Billing.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCore.Billing.Infrastructure.Persistence;

public sealed class BillingUnitOfWork : IUnitOfWork
{
    private readonly BillingDbContext _dbContext;

    public BillingUnitOfWork(BillingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _dbContext.ChangeTracker.Clear();
            throw new ConcurrentPaymentException(exception);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "pk_processed_messages"
            })
        {
            _dbContext.ChangeTracker.Clear();
            throw new DuplicateProcessedMessageException(exception);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_invoices_appointment_id"
            })
        {
            _dbContext.ChangeTracker.Clear();
            throw new DuplicateInvoiceForAppointmentException(exception);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_notification_logs_source_template"
            })
        {
            _dbContext.ChangeTracker.Clear();
            throw new DuplicateNotificationLogException(exception);
        }
    }
}
