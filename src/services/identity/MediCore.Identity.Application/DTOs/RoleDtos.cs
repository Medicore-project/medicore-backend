namespace MediCore.Identity.Application.DTOs;

public record RoleResponse(
    Guid Id,
    string Name,
    string Description);

public record AssignRoleRequest(
    Guid RoleId);
