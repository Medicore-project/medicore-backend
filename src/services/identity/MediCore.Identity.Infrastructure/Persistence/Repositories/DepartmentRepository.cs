using MediCore.Identity.Application.Entities;
using MediCore.Identity.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Identity.Infrastructure.Persistence.Repositories;

public class DepartmentRepository : IDepartmentRepository
{
    private readonly IdentityDbContext _context;

    public DepartmentRepository(IdentityDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Department>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Departments
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<Department?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.Departments
            .FirstOrDefaultAsync(d => d.Name.ToLower() == name.ToLower(), cancellationToken);
    }

    public async Task AddAsync(Department department, CancellationToken cancellationToken = default)
    {
        await _context.Departments.AddAsync(department, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Department department, CancellationToken cancellationToken = default)
    {
        _context.Departments.Update(department);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Department department, CancellationToken cancellationToken = default)
    {
        department.IsDeleted = true;
        _context.Departments.Update(department);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> HasActiveStaffMembersAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        // TODO: Implement actual check once StaffProfile entity is added in SCRUM-12.
        // E.g. return _context.StaffProfiles.AnyAsync(s => s.DepartmentId == departmentId && s.IsActive, cancellationToken);
        return Task.FromResult(false);
    }
}
