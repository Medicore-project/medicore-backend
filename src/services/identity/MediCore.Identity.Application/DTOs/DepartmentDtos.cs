using System.ComponentModel.DataAnnotations;

namespace MediCore.Identity.Application.DTOs;

public record DepartmentResponse(
    Guid Id,
    string Name,
    string Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateDepartmentRequest(
    [Required][StringLength(100, MinimumLength = 1)] string Name,
    [StringLength(500)] string Description);

public record UpdateDepartmentRequest(
    [Required][StringLength(100, MinimumLength = 1)] string Name,
    [StringLength(500)] string Description,
    bool IsActive);
