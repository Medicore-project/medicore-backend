using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Application.DTOs;
using Microsoft.EntityFrameworkCore;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Infrastructure.Persistence.Repositories;

public sealed class PatientRepository : IPatientRepository, IPatientSearchRepository
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

    public async Task<PatientSearchResponse> SearchAsync(
        string searchTerm,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Patients
            .AsNoTracking()
            .Where(patient => !patient.IsDeleted);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var escapedTerm = EscapeLikePattern(searchTerm);
            var containsPattern = $"%{escapedTerm}%";
            var normalizedIdentifier = searchTerm.ToUpperInvariant();

            query = query
                .Where(patient =>
                    EF.Functions.ILike(patient.FirstName, containsPattern, @"\") ||
                    EF.Functions.ILike(patient.LastName, containsPattern, @"\") ||
                    EF.Functions.ILike(patient.FirstName + " " + patient.LastName, containsPattern, @"\") ||
                    EF.Functions.ILike(patient.Nic, containsPattern, @"\") ||
                    EF.Functions.ILike(patient.PatientNumber, containsPattern, @"\"))
                .OrderByDescending(patient =>
                    patient.Nic == normalizedIdentifier || patient.PatientNumber == normalizedIdentifier)
                .ThenBy(patient => patient.LastName)
                .ThenBy(patient => patient.FirstName)
                .ThenBy(patient => patient.PatientNumber);
        }
        else
        {
            query = query
                .OrderBy(patient => patient.LastName)
                .ThenBy(patient => patient.FirstName)
                .ThenBy(patient => patient.PatientNumber);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(patient => new PatientSearchResult(
                patient.Id,
                patient.PatientNumber,
                patient.Nic,
                patient.FirstName + " " + patient.LastName,
                patient.DateOfBirth,
                patient.Phone,
                patient.Email,
                patient.District))
            .ToListAsync(cancellationToken);

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PatientSearchResponse(
            items,
            totalCount,
            page,
            pageSize,
            totalPages,
            page > 1 && totalCount > 0,
            page < totalPages);
    }

    private static string EscapeLikePattern(string value) => value
        .Replace(@"\", @"\\", StringComparison.Ordinal)
        .Replace("%", @"\%", StringComparison.Ordinal)
        .Replace("_", @"\_", StringComparison.Ordinal);
}
