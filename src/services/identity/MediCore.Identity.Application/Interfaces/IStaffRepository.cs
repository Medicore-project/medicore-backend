using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Entities;

namespace MediCore.Identity.Application.Interfaces;

public interface IStaffRepository
{
    /// <param name="specialization">
    /// Exact specialization name, for "who holds this specialization" lookups. Matched whole rather
    /// than as a substring like <paramref name="searchTerm"/> does, so "Cardiology" does not also
    /// return "Pediatric Cardiology".
    /// </param>
    Task<PagedResult<StaffResponse>> GetPagedAsync(
        int page,
        int pageSize,
        string? searchTerm,
        Guid? departmentId,
        string? role,
        bool? isActive,
        string? specialization = null,
        CancellationToken cancellationToken = default);

    Task<StaffResponse?> GetByIdAsync(Guid staffId, CancellationToken cancellationToken = default);

    Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<StaffResponse> CreateStaffAsync(
        CreateStaffRequest request,
        string passwordHash,
        CancellationToken cancellationToken = default);

    Task<StaffResponse?> UpdateStaffAsync(
        Guid staffId,
        UpdateStaffRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeactivateStaffAsync(Guid staffId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a staff.updated outbox row for every doctor, for consumers backfilling their cache.
    /// </summary>
    /// <returns>The number of events queued.</returns>
    Task<int> QueueDoctorRepublishAsync(CancellationToken cancellationToken = default);
}
