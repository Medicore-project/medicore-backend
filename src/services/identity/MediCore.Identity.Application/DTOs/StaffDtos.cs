using System.ComponentModel.DataAnnotations;

namespace MediCore.Identity.Application.DTOs;

public record CreateStaffRequest(
    [Required][EmailAddress][StringLength(256)] string Email,
    [Required][StringLength(100, MinimumLength = 8)] string Password,
    [Required][StringLength(50)] string Role,
    [Required][StringLength(100, MinimumLength = 1)] string FirstName,
    [Required][StringLength(100, MinimumLength = 1)] string LastName,
    [StringLength(50)] string? Phone,
    [StringLength(100)] string? Specialization,
    [Required] Guid DepartmentId);

public record UpdateStaffRequest(
    [Required][StringLength(100, MinimumLength = 1)] string FirstName,
    [Required][StringLength(100, MinimumLength = 1)] string LastName,
    [StringLength(50)] string? Phone,
    [StringLength(100)] string? Specialization,
    [Required] Guid DepartmentId,
    bool IsActive);

/// <summary>
/// An Admin setting a staff member's password for them. There is no old password to confirm: the
/// person asking is not the account's owner, and usually the owner has forgotten it.
/// </summary>
public record ChangeStaffPasswordRequest(
    [Required][StringLength(100, MinimumLength = 8)] string NewPassword);

public record StaffResponse(
    Guid Id,
    Guid UserId,
    string Email,
    string Role,
    string FirstName,
    string LastName,
    string FullName,
    string? Phone,
    string? Specialization,
    Guid DepartmentId,
    DateTime HireDate,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

public record RepublishDoctorsResponse(int Queued);
