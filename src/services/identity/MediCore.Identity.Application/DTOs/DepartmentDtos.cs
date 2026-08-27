namespace MediCore.Identity.Application.DTOs;

public record DepartmentResponse(
    Guid Id,
    string Name,
    string Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateDepartmentRequest(
    string Name,
    string Description);

public record UpdateDepartmentRequest(
    string Name,
    string Description,
    bool IsActive);
