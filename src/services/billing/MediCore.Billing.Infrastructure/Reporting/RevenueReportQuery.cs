using System.Data;
using System.Text;
using MediCore.Billing.Application.DTOs;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace MediCore.Billing.Infrastructure.Reporting;

/// <summary>Cash-basis revenue from payments, grouped in Colombo calendar days.</summary>
public sealed class RevenueReportQuery : IRevenueReportQuery
{
    private readonly BillingDbContext _db;
    private static readonly TimeZoneInfo Colombo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");

    public RevenueReportQuery(BillingDbContext db) => _db = db;

    public async Task<IReadOnlyList<RevenueSourceRow>> QueryAsync(
        RevenueQueryCriteria criteria, CancellationToken cancellationToken = default)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = BuildCommand(connection, criteria);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<RevenueSourceRow>();
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new RevenueSourceRow(reader.GetFieldValue<DateOnly>(0),
                    reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2),
                    reader.GetString(3), reader.GetDecimal(4), reader.GetInt64(5)));
            return rows;
        }
        finally
        {
            if (close) await connection.CloseAsync();
        }
    }

    internal static NpgsqlCommand BuildCommand(NpgsqlConnection connection, RevenueQueryCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.From > criteria.To || criteria.To == DateOnly.MaxValue)
            throw new ArgumentException("Invalid date range.", nameof(criteria));

        var command = connection.CreateCommand();
        var sql = new StringBuilder("""
            SELECT (p."RecordedAtUtc" AT TIME ZONE 'Asia/Colombo')::date AS payment_date,
                   i."DepartmentId", p."Method", i."Currency",
                   SUM(p."Amount") AS amount, COUNT(*) AS payment_count
            FROM medicore_billing.payments AS p
            INNER JOIN medicore_billing.invoices AS i ON i."InvoiceId" = p."InvoiceId"
            WHERE p."RecordedAtUtc" >= @fromUtc AND p."RecordedAtUtc" < @toUtc
            """);
        command.Parameters.AddWithValue("fromUtc", NpgsqlDbType.TimestampTz, StartUtc(criteria.From));
        command.Parameters.AddWithValue("toUtc", NpgsqlDbType.TimestampTz, StartUtc(criteria.To.AddDays(1)));
        if (criteria.DepartmentId.HasValue)
        {
            sql.AppendLine(" AND i.\"DepartmentId\" = @departmentId");
            command.Parameters.AddWithValue("departmentId", NpgsqlDbType.Uuid, criteria.DepartmentId.Value);
        }
        if (criteria.PaymentMethod is not null)
        {
            sql.AppendLine(" AND p.\"Method\" = @method");
            command.Parameters.AddWithValue("method", NpgsqlDbType.Varchar, criteria.PaymentMethod);
        }
        if (criteria.Currency is not null)
        {
            sql.AppendLine(" AND i.\"Currency\" = @currency");
            command.Parameters.AddWithValue("currency", NpgsqlDbType.Varchar, criteria.Currency);
        }
        sql.AppendLine();
        sql.AppendLine("GROUP BY payment_date, i.\"DepartmentId\", p.\"Method\", i.\"Currency\"");
        sql.AppendLine("ORDER BY payment_date, i.\"DepartmentId\", p.\"Method\", i.\"Currency\"");
        command.CommandText = sql.ToString();
        return command;
    }

    private static DateTime StartUtc(DateOnly date) => TimeZoneInfo.ConvertTimeToUtc(
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Colombo);
}
