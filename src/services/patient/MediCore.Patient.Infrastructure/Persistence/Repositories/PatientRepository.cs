using MediCore.Patient.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Infrastructure.Persistence.Repositories;

public sealed class PatientRepository : IPatientRepository
{
    private readonly PatientDbContext _dbContext;

    public PatientRepository(PatientDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<PatientEntity?> FindByNicAsync(
        string normalizedNic,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var query = includeArchived
            ? _dbContext.Patients.IgnoreQueryFilters()
            : _dbContext.Patients;

        return query
            .AsNoTracking()
            .SingleOrDefaultAsync(patient => patient.Nic == normalizedNic, cancellationToken);
    }

    public Task AddAsync(
        PatientEntity patient,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Patients.AddAsync(patient, cancellationToken).AsTask();
    }

    public Task<PatientEntity?> GetByIdAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Patients
            .AsNoTracking()
            .SingleOrDefaultAsync(patient => patient.Id == patientId, cancellationToken);
    }

    public Task<PatientEntity?> GetTrackedByIdAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Patients
            .SingleOrDefaultAsync(patient => patient.Id == patientId, cancellationToken);
    }
}
