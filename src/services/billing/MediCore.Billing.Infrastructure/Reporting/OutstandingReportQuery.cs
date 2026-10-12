using System.Data;
using System.Text;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Entities;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace MediCore.Billing.Infrastructure.Reporting;

public sealed class OutstandingReportQuery : IOutstandingReportQuery
{
    private readonly BillingDbContext _db;
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");

    public OutstandingReportQuery(BillingDbContext db) => _db = db;

    public async Task<IReadOnlyList<OutstandingSourceRow>> QueryAsync(
        OutstandingQueryCriteria criteria, CancellationToken cancellationToken = default)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = BuildCommand(connection, criteria);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<OutstandingSourceRow>();
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new OutstandingSourceRow(reader.GetGuid(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3),
                    reader.GetDateTime(4), reader.GetDateTime(5), reader.GetDecimal(6), reader.GetDecimal(7)));
            return rows;
        }
        finally
        {
            if (close) await connection.CloseAsync();
        }
    }

    internal static NpgsqlCommand BuildCommand(NpgsqlConnection connection, OutstandingQueryCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.From > criteria.To || criteria.To == DateOnly.MaxValue)
            throw new ArgumentException("Invalid invoice issue date range.", nameof(criteria));

        var command = connection.CreateCommand();
        var sql = new StringBuilder("""
            SELECT i."InvoiceId", i."InvoiceNumber", i."DepartmentId", i."Currency",
                   i."IssuedAtUtc", i."FinalizedAtUtc", i."Total", i."AmountPaid"
            FROM medicore_billing.invoices AS i
            WHERE i."Status" = @status
              AND i."FinalizedAtUtc" IS NOT NULL
              AND i."FinalizedAtUtc" <= @asOfUtc
              AND i."IssuedAtUtc" <= @asOfUtc
              AND i."Total" > i."AmountPaid"
            """);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Varchar, InvoiceStatus.Payable);
        command.Parameters.AddWithValue("asOfUtc", NpgsqlDbType.TimestampTz, criteria.AsOfUtc);
        if (criteria.DepartmentId.HasValue)
        {
            sql.AppendLine(" AND i.\"DepartmentId\" = @departmentId");
            command.Parameters.AddWithValue("departmentId", NpgsqlDbType.Uuid, criteria.DepartmentId.Value);
        }
        if (criteria.Currency is not null)
        {
            sql.AppendLine(" AND i.\"Currency\" = @currency");
            command.Parameters.AddWithValue("currency", NpgsqlDbType.Varchar, criteria.Currency);
        }
        if (criteria.From.HasValue)
        {
            sql.AppendLine(" AND i.\"IssuedAtUtc\" >= @fromUtc");
            command.Parameters.AddWithValue("fromUtc", NpgsqlDbType.TimestampTz, StartUtc(criteria.From.Value));
        }
        if (criteria.To.HasValue)
        {
            sql.AppendLine(" AND i.\"IssuedAtUtc\" < @toUtc");
            command.Parameters.AddWithValue("toUtc", NpgsqlDbType.TimestampTz, StartUtc(criteria.To.Value.AddDays(1)));
        }
        sql.AppendLine("ORDER BY i.\"FinalizedAtUtc\", i.\"InvoiceNumber\"");
        command.CommandText = sql.ToString();
        return command;
    }

    private static DateTime StartUtc(DateOnly date) => TimeZoneInfo.ConvertTimeToUtc(
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Colombo);
}
