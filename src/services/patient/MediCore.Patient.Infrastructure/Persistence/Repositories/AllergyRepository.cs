using MediCore.Patient.Application.Entities;
using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using AllergyEntity = MediCore.Patient.Application.Entities.Allergy;

namespace MediCore.Patient.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAllergyRepository"/>.
/// All queries respect the global soft-delete query filter defined in
/// <see cref="AllergyConfiguration"/> (<c>!IsDeleted</c>).
/// </summary>
public sealed class AllergyRepository : IAllergyRepository
{
    private readonly PatientDbContext _db;

    public AllergyRepository(PatientDbContext db) => _db = db;

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task AddAsync(AllergyEntity allergy, CancellationToken cancellationToken = default)
    {
        await _db.Allergies.AddAsync(allergy, cancellationToken);
    }

    // ── Reads ─────────────────────────────────────────────────────────────────

    public Task<AllergyEntity?> GetByIdAsync(
        Guid patientId,
        Guid allergyId,
        CancellationToken cancellationToken = default) =>
        _db.Allergies
            .AsNoTracking()
            .Where(a => a.PatientId == patientId && a.AllergyId == allergyId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<AllergyEntity?> GetTrackedByIdAsync(
        Guid patientId,
        Guid allergyId,
        CancellationToken cancellationToken = default) =>
        _db.Allergies
            .Where(a => a.PatientId == patientId && a.AllergyId == allergyId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AllergyEntity>> GetAllAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Allergies
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.RecordedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AllergyEntity>> GetActiveAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Allergies
            .AsNoTracking()
            .Where(a => a.PatientId == patientId && a.Status == AllergyStatus.Active)
            .OrderByDescending(a => a.RecordedAtUtc)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Case-insensitive substring conflict check: returns the first active allergy
    /// whose <c>Allergen</c> is contained within the supplied <paramref name="drug"/>
    /// string, or vice-versa.
    /// <para>
    /// Two directions are tested to handle both "Penicillin" matching "Amoxicillin/Penicillin"
    /// and "Amoxicillin" matching allergen "Amox".
    /// </para>
    /// </summary>
    public Task<AllergyEntity?> CheckConflictAsync(
        Guid patientId,
        string drug,
        CancellationToken cancellationToken = default) =>
        BuildConflictQuery(patientId, drug)
            .FirstOrDefaultAsync(cancellationToken);

    internal IQueryable<AllergyEntity> BuildConflictQuery(Guid patientId, string drug) =>
        _db.Allergies
            .AsNoTracking()
            .Where(a =>
                a.PatientId == patientId &&
                a.Status == AllergyStatus.Active &&
                (
                    EF.Functions.ILike(a.Allergen, $"%{drug}%") ||
                    EF.Functions.ILike(drug, "%" + a.Allergen + "%")
                ))
            .OrderByDescending(a => a.RecordedAtUtc);
}
