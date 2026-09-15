using MediCore.Patient.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using MedicalRecordEntity = MediCore.Patient.Application.Entities.MedicalRecord;

namespace MediCore.Patient.Infrastructure.Persistence.Repositories;

public sealed class MedicalRecordRepository : IMedicalRecordRepository
{
    private readonly PatientDbContext _dbContext;

    public MedicalRecordRepository(PatientDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(MedicalRecordEntity record, CancellationToken cancellationToken = default) =>
        _dbContext.MedicalRecords.AddAsync(record, cancellationToken).AsTask();

    public Task<MedicalRecordEntity?> GetCurrentAsync(
        Guid patientId,
        Guid recordId,
        CancellationToken cancellationToken = default) =>
        _dbContext.MedicalRecords
            .AsNoTracking()
            .Include(record => record.Conditions)
            .SingleOrDefaultAsync(record =>
                record.PatientId == patientId &&
                record.RecordId == recordId &&
                record.IsCurrent,
                cancellationToken);

    public Task<MedicalRecordEntity?> GetTrackedCurrentAsync(
        Guid patientId,
        Guid recordId,
        CancellationToken cancellationToken = default) =>
        _dbContext.MedicalRecords
            .SingleOrDefaultAsync(record =>
                record.PatientId == patientId &&
                record.RecordId == recordId &&
                record.IsCurrent,
                cancellationToken);

    public async Task<(IReadOnlyList<MedicalRecordEntity> Items, int TotalCount)> GetCurrentPageAsync(
        Guid patientId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.MedicalRecords
            .AsNoTracking()
            .Include(record => record.Conditions)
            .Where(record => record.PatientId == patientId && record.IsCurrent);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(record => record.AuthoredAtUtc)
            .ThenByDescending(record => record.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<MedicalRecordEntity>> GetVersionsAsync(
        Guid patientId,
        Guid recordId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.MedicalRecords
            .AsNoTracking()
            .Include(record => record.Conditions)
            .Where(record => record.PatientId == patientId && record.RecordId == recordId)
            .OrderByDescending(record => record.Version)
            .ToListAsync(cancellationToken);
}
