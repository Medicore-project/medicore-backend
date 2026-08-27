using MediCore.Identity.Application.Entities;

namespace MediCore.Identity.Application.Interfaces;

public interface IDepartmentRepository
{
    Task<IReadOnlyList<Department>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Department?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task AddAsync(Department department, CancellationToken cancellationToken = default);
    Task UpdateAsync(Department department, CancellationToken cancellationToken = default);
    Task DeleteAsync(Department department, CancellationToken cancellationToken = default);
    Task<bool> HasActiveStaffMembersAsync(Guid departmentId, CancellationToken cancellationToken = default);
}
