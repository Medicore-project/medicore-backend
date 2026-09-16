using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using PrescriptionEntity = MediCore.Patient.Application.Entities.Prescription;

namespace MediCore.Patient.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPrescriptionRepository"/>.
/// All queries are scoped to a single patient to prevent cross-patient data leakage.
/// The global <c>HasQueryFilter(!IsDeleted)</c> on <see cref="PrescriptionEntity"/>
/// automatically excludes soft-deleted rows from every query in this class.
/// </summary>
public sealed class PrescriptionRepository : IPrescriptionRepository
{
    private readonly PatientDbContext _dbContext;

    public PrescriptionRepository(PatientDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(PrescriptionEntity prescription, CancellationToken cancellationToken = default) =>
        _dbContext.Prescriptions.AddAsync(prescription, cancellationToken).AsTask();

    public Task<PrescriptionEntity?> GetByIdAsync(
        Guid patientId,
        Guid prescriptionId,
        CancellationToken cancellationToken = default) =>
        _dbContext.Prescriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.PatientId == patientId && p.PrescriptionId == prescriptionId,
                cancellationToken);

    public Task<PrescriptionEntity?> GetTrackedByIdAsync(
        Guid patientId,
        Guid prescriptionId,
        CancellationToken cancellationToken = default) =>
        _dbContext.Prescriptions
            .SingleOrDefaultAsync(
                p => p.PatientId == patientId && p.PrescriptionId == prescriptionId,
                cancellationToken);

    public async Task<IReadOnlyList<PrescriptionEntity>> GetActiveAsync(
        Guid patientId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Prescriptions
            .AsNoTracking()
            .Where(p => p.PatientId == patientId && p.Status == PrescriptionStatus.Active)
            .OrderByDescending(p => p.PrescribedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PrescriptionEntity>> GetHistoryAsync(
        Guid patientId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Prescriptions
            .AsNoTracking()
            .Where(p => p.PatientId == patientId && p.Status == PrescriptionStatus.Completed)
            .OrderByDescending(p => p.CompletedAtUtc)
            .ThenByDescending(p => p.PrescribedAtUtc)
            .ToListAsync(cancellationToken);
}
