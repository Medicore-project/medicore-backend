using MediCore.Patient.Application.Exceptions;
using MediCore.Patient.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCore.Patient.Infrastructure.Persistence;

public sealed class PatientUnitOfWork : IUnitOfWork
{
    private const string NicConstraintName = "ux_patients_nic";
    private static readonly string[] MedicalRecordVersionConstraints =
    [
        "ux_medical_records_current_record",
        "ux_medical_records_record_version"
    ];
    private readonly PatientDbContext _dbContext;

    public PatientUnitOfWork(PatientDbContext dbContext)
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
                ConstraintName: not null
            } postgresException &&
            MedicalRecordVersionConstraints.Contains(postgresException.ConstraintName, StringComparer.Ordinal))
        {
            _dbContext.ChangeTracker.Clear();
            throw new MedicalRecordVersionConflictException(exception);
        }
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
