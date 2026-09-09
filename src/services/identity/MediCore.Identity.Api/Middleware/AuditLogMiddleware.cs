using System.Diagnostics;
using System.Security.Claims;
using MediCore.Identity.Application.Entities;
using MediCore.Identity.Infrastructure.Persistence;

namespace MediCore.Identity.Api.Middleware;

public class AuditLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLogMiddleware> _logger;

    public AuditLogMiddleware(RequestDelegate next, ILogger<AuditLogMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IdentityDbContext dbContext)
    {
        // Only audit specific endpoints (e.g., Staff Management) and only mutating methods
        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;

        var shouldAudit = (path.StartsWith("/api/staff", StringComparison.OrdinalIgnoreCase) || 
                           path.StartsWith("/api/departments", StringComparison.OrdinalIgnoreCase) || 
                           path.StartsWith("/api/roles", StringComparison.OrdinalIgnoreCase)) &&
                          (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsDelete(method));

        if (!shouldAudit)
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        
        // Execute the next middleware/controller
        await _next(context);
        
        sw.Stop();

        try
        {
            var userIdString = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userEmail = context.User.FindFirstValue(ClaimTypes.Email) ?? "anonymous";
            var userRole = context.User.FindFirstValue(ClaimTypes.Role) ?? "Unknown";
            
            Guid userId = Guid.Empty;
            if (Guid.TryParse(userIdString, out var parsedId))
            {
                userId = parsedId;
            }

            // Simple entity extraction from path: /api/staff/123 -> EntityType: staff, EntityId: 123
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var entityType = segments.Length > 1 ? segments[1] : "unknown";
            Guid entityId = Guid.Empty;
            if (segments.Length > 2 && Guid.TryParse(segments[2], out var parsedEntityId))
            {
                entityId = parsedEntityId;
            }

            var auditLog = new AuditLog
            {
                UserId = userId,
                UserEmail = userEmail,
                Role = userRole,
                ActionType = method.ToUpper() switch
                {
                    "POST"   => "Create",
                    "PUT"    => "Update",
                    "PATCH"  => "Update",
                    "DELETE" => "Delete",
                    _        => method
                },
                EntityType = entityType,
                EntityId = entityId,
                OccurredAtUtc = DateTime.UtcNow,
                IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            };

            dbContext.AuditLogs.Add(auditLog);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Do not block the response if audit logging fails
            _logger.LogError(ex, "Failed to write audit log.");
        }
    }
}
