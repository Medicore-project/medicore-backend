using MediCore.Identity.Application.Entities;
using MediCore.Identity.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Identity.Infrastructure.Persistence.Repositories;

public class SpecializationRepository : ISpecializationRepository
{
    private readonly IdentityDbContext _context;

    public SpecializationRepository(IdentityDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Specialization>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Specializations
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Specialization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Specializations
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Specialization?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.Specializations
            .FirstOrDefaultAsync(s => s.Name.ToLower() == name.ToLower(), cancellationToken);
    }

    public async Task AddAsync(Specialization specialization, CancellationToken cancellationToken = default)
    {
        await _context.Specializations.AddAsync(specialization, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Specialization specialization, CancellationToken cancellationToken = default)
    {
        _context.Specializations.Update(specialization);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Specialization specialization, CancellationToken cancellationToken = default)
    {
        specialization.IsDeleted = true;
        _context.Specializations.Update(specialization);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> HasActiveStaffMembersAsync(Guid specializationId, CancellationToken cancellationToken = default)
    {
        // TODO: Implement actual check once StaffProfile entity is added in SCRUM-12.
        return Task.FromResult(false);
    }
}
