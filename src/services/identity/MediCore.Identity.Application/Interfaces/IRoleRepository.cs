using MediCore.Identity.Application.DTOs;

namespace MediCore.Identity.Application.Interfaces;

public interface IRoleRepository
{
    Task<IReadOnlyList<RoleResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RoleResponse?> GetByIdAsync(Guid roleId, CancellationToken cancellationToken = default);
    Task<bool> AssignRoleToStaffAsync(Guid staffId, Guid roleId, CancellationToken cancellationToken = default);
}
