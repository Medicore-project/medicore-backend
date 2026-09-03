using MediCore.Identity.Application.Entities;

namespace MediCore.Identity.Application.Interfaces;

public interface ISpecializationRepository
{
    Task<IReadOnlyList<Specialization>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Specialization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Specialization?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task AddAsync(Specialization specialization, CancellationToken cancellationToken = default);
    Task UpdateAsync(Specialization specialization, CancellationToken cancellationToken = default);
    Task DeleteAsync(Specialization specialization, CancellationToken cancellationToken = default);
    Task<bool> HasActiveStaffMembersAsync(Guid specializationId, CancellationToken cancellationToken = default);
}
