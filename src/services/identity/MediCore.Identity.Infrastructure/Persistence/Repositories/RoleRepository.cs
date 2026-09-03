using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Entities;
using MediCore.Identity.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Identity.Infrastructure.Persistence.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly IdentityDbContext _context;

    public RoleRepository(IdentityDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<RoleResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleResponse(r.Id, r.Name, r.Description))
            .ToListAsync(cancellationToken);
    }

    public async Task<RoleResponse?> GetByIdAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await _context.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);

        return role == null ? null : new RoleResponse(role.Id, role.Name, role.Description);
    }

    public async Task<bool> AssignRoleToStaffAsync(Guid staffId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var staff = await _context.StaffProfiles
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == staffId, cancellationToken);

        if (staff == null || staff.User == null)
            return false;

        var role = await _context.Roles
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);

        if (role == null)
            return false;

        var existingUserRole = await _context.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == staff.UserId, cancellationToken);

        if (existingUserRole != null)
        {
            existingUserRole.RoleId = roleId;
        }
        else
        {
            await _context.UserRoles.AddAsync(new UserRole
            {
                UserId = staff.UserId,
                RoleId = roleId,
                AssignedAt = DateTime.UtcNow
            }, cancellationToken);
        }

        staff.User.Role = role.Name;

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
