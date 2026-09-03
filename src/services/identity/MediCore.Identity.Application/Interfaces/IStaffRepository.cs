using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Entities;

namespace MediCore.Identity.Application.Interfaces;

public interface IStaffRepository
{
    Task<PagedResult<StaffResponse>> GetPagedAsync(
        int page,
        int pageSize,
        string? searchTerm,
        Guid? departmentId,
        string? role,
        bool? isActive,
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
}
