namespace MediCore.Identity.Application.DTOs;

public sealed record AuditReportFilter(
    Guid? UserId,
    string? Role,
    string? ActionType,
    DateTime? From,
    DateTime? To,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditReportRow(
    Guid Id,
    Guid UserId,
    string UserEmail,
    string Role,
    string ActionType,
    string EntityType,
    Guid EntityId,
    DateTime OccurredAtUtc);
