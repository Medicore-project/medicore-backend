using System.Text;
using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Interfaces;
using MediCore.Identity.Infrastructure.Persistence;
using Npgsql;

namespace MediCore.Identity.Infrastructure.Reporting;

public sealed class AuditReportQuery
    : ReportQueryBase<AuditReportFilter, AuditReportRow>,
      IReportQuery<AuditReportFilter, AuditReportRow>
{
    public AuditReportQuery(IdentityDbContext dbContext) : base(dbContext) { }

    public Task<IReadOnlyList<AuditReportRow>> QueryAsync(
        AuditReportFilter filter,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(filter, cancellationToken);

    protected override void BuildQuery(NpgsqlCommand cmd, AuditReportFilter filter)
    {
        var sql = new StringBuilder(@"
            SELECT al.id, al.user_id, u.email, u.role, al.action_type,
                   al.entity_type, al.entity_id, al.occurred_at_utc
            FROM   medicore_identity.audit_logs al
            JOIN   medicore_identity.users u ON u.id = al.user_id
            WHERE  1 = 1");

        if (filter.UserId.HasValue)
        {
            sql.Append(" AND al.user_id = @userId");
            cmd.Parameters.AddWithValue("@userId", filter.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Role))
        {
            sql.Append(" AND u.role = @role");
            cmd.Parameters.AddWithValue("@role", filter.Role);
        }

        if (!string.IsNullOrWhiteSpace(filter.ActionType))
        {
            sql.Append(" AND al.action_type = @actionType");
            cmd.Parameters.AddWithValue("@actionType", filter.ActionType);
        }

        if (filter.From.HasValue)
        {
            sql.Append(" AND al.occurred_at_utc >= @from");
            cmd.Parameters.AddWithValue("@from", filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            sql.Append(" AND al.occurred_at_utc <= @to");
            cmd.Parameters.AddWithValue("@to", filter.To.Value);
        }

        sql.Append(" ORDER BY al.occurred_at_utc DESC");
        sql.Append($" LIMIT {filter.PageSize} OFFSET {(filter.Page - 1) * filter.PageSize}");

        cmd.CommandText = sql.ToString();
    }

    protected override AuditReportRow MapRow(NpgsqlDataReader reader) => new(
        Id: reader.GetGuid(0),
        UserId: reader.GetGuid(1),
        UserEmail: reader.GetString(2),
        Role: reader.GetString(3),
        ActionType: reader.GetString(4),
        EntityType: reader.GetString(5),
        EntityId: reader.GetGuid(6),
        OccurredAtUtc: reader.GetDateTime(7));
}
