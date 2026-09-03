using System.Data;
using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MediCore.Identity.Infrastructure.Reporting;

public class AuditReportService : IAuditReportService
{
    private readonly string _connectionString;

    public AuditReportService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("IdentityDatabase") 
            ?? throw new InvalidOperationException("Connection string 'IdentityDatabase' not found.");
    }

    public async Task<IEnumerable<AuditReportRow>> GetAuditReportAsync(AuditReportFilter filter, CancellationToken cancellationToken = default)
    {
        var results = new List<AuditReportRow>();

        using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var query = @"
            SELECT ""Id"", ""UserId"", ""UserEmail"", ""Role"", ""ActionType"", ""EntityType"", ""EntityId"", ""OccurredAtUtc""
            FROM ""AuditLogs""
            WHERE 1=1";

        using var command = new NpgsqlCommand(query, connection);

        if (filter.UserId.HasValue)
        {
            command.CommandText += @" AND ""UserId"" = @UserId";
            command.Parameters.AddWithValue("UserId", filter.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.ActionType))
        {
            command.CommandText += @" AND ""ActionType"" = @ActionType";
            command.Parameters.AddWithValue("ActionType", filter.ActionType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Role))
        {
            command.CommandText += @" AND ""Role"" = @Role";
            command.Parameters.AddWithValue("Role", filter.Role);
        }

        if (filter.From.HasValue)
        {
            command.CommandText += @" AND ""OccurredAtUtc"" >= @From";
            command.Parameters.AddWithValue("From", filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            command.CommandText += @" AND ""OccurredAtUtc"" <= @To";
            command.Parameters.AddWithValue("To", filter.To.Value);
        }

        command.CommandText += @" ORDER BY ""OccurredAtUtc"" DESC";

        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new AuditReportRow(
                Id: reader.GetGuid(reader.GetOrdinal("Id")),
                UserId: reader.GetGuid(reader.GetOrdinal("UserId")),
                UserEmail: reader.GetString(reader.GetOrdinal("UserEmail")),
                Role: reader.GetString(reader.GetOrdinal("Role")),
                ActionType: reader.GetString(reader.GetOrdinal("ActionType")),
                EntityType: reader.GetString(reader.GetOrdinal("EntityType")),
                EntityId: reader.GetGuid(reader.GetOrdinal("EntityId")),
                OccurredAtUtc: reader.GetDateTime(reader.GetOrdinal("OccurredAtUtc"))
            ));
        }

        return results;
    }
}
