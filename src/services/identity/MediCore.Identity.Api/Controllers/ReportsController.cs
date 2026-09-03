using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Identity.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Admin")] // Only Admin can view audit logs
public class ReportsController : ControllerBase
{
    private readonly IAuditReportService _auditReportService;

    public ReportsController(IAuditReportService auditReportService)
    {
        _auditReportService = auditReportService;
    }

    [HttpGet("audit")]
    public async Task<ActionResult<IEnumerable<AuditReportRow>>> GetAuditReport(
        [FromQuery] Guid? userId,
        [FromQuery] string? role,
        [FromQuery] string? actionType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var filter = new AuditReportFilter(userId, role, actionType, from, to);
        var report = await _auditReportService.GetAuditReportAsync(filter, cancellationToken);
        
        return Ok(report);
    }
}
