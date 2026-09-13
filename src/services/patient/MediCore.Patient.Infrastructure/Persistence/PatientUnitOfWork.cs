using MediCore.Patient.Application.Exceptions;
using MediCore.Patient.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCore.Patient.Infrastructure.Persistence;

public sealed class PatientUnitOfWork : IUnitOfWork
{
    private const string NicConstraintName = "ux_patients_nic";
    private readonly PatientDbContext _dbContext;

    public PatientUnitOfWork(PatientDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveRegistrationAsync(
        string duplicateNic,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: NicConstraintName
            })
        {
            _dbContext.ChangeTracker.Clear();
            throw new DuplicateNicException(duplicateNic, exception);
        }
    }
}
