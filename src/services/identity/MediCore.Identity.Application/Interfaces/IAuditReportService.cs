using MediCore.Identity.Application.DTOs;

namespace MediCore.Identity.Application.Interfaces;

public interface IAuditReportService
{
    Task<IEnumerable<AuditReportRow>> GetAuditReportAsync(AuditReportFilter filter, CancellationToken cancellationToken = default);
}
