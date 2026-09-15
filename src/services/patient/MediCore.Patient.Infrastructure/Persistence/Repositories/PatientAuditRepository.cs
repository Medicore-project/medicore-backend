using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Interfaces;

namespace MediCore.Patient.Infrastructure.Persistence.Repositories;

public sealed class PatientAuditRepository : IPatientAuditRepository
{
    private readonly PatientDbContext _dbContext;

    public PatientAuditRepository(PatientDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(PatientAuditLog auditLog, CancellationToken cancellationToken = default)
    {
        return _dbContext.PatientAuditLogs.AddAsync(auditLog, cancellationToken).AsTask();
    }
}
