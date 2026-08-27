namespace MediCore.Identity.Application.DTOs;

public record SpecializationResponse(
    Guid Id,
    string Name,
    string Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateSpecializationRequest(
    string Name,
    string Description);

public record UpdateSpecializationRequest(
    string Name,
    string Description,
    bool IsActive);
