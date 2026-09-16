using System.Data;
using System.Text;
using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace MediCore.Patient.Infrastructure.Reporting;

public sealed class DemographicsReportQuery : IDemographicsReportQuery
{
    private readonly PatientDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public DemographicsReportQuery(PatientDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<DemographicsReportSourceRow>> QueryAsync(
        DemographicsReportFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var asOfDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
            await using var command = BuildCommand(connection, filter, asOfDate);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var rows = new List<DemographicsReportSourceRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(MapRow(reader));
            }

            return rows;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    internal static NpgsqlCommand BuildCommand(
        NpgsqlConnection connection,
        DemographicsReportFilter filter,
        DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.From.HasValue && filter.To.HasValue && filter.From > filter.To)
        {
            throw new ArgumentException("To date must be on or after from date.", nameof(filter));
        }

        if (filter.To == DateOnly.MaxValue)
        {
            throw new ArgumentException("To date must allow an exclusive next-day boundary.", nameof(filter));
        }

        string? requestedAgeBand = null;
        if (!string.IsNullOrWhiteSpace(filter.AgeBand))
        {
            if (!DemographicsReportOptions.TryGetAgeRange(filter.AgeBand, out _))
            {
                throw new ArgumentException("Unsupported demographics age band.", nameof(filter));
            }

            requestedAgeBand = filter.AgeBand.Trim();
        }

        var sql = new StringBuilder(
            """
            WITH patient_demographics AS (
                SELECT p."Id",
                       p."PatientNumber",
                       p."Gender",
                       p."District",
                       patient_age.age,
                       CASE
                           WHEN patient_age.age BETWEEN 0 AND 17 THEN '0-17'
                           WHEN patient_age.age BETWEEN 18 AND 34 THEN '18-34'
                           WHEN patient_age.age BETWEEN 35 AND 49 THEN '35-49'
                           WHEN patient_age.age BETWEEN 50 AND 64 THEN '50-64'
                           ELSE '65+'
                       END AS age_band
                FROM medicore_patient.patients AS p
                CROSS JOIN LATERAL (
                    SELECT EXTRACT(YEAR FROM AGE(@asOfDate, p."DateOfBirth"))::integer AS age
                ) AS patient_age
                WHERE p."IsDeleted" = false
            )
            SELECT p."Id",
                   p."PatientNumber",
                   p.age,
                   p.age_band,
                   p."Gender",
                   p."District",
                   mr."RecordId",
                   mr."VisitReference",
                   mr."AuthoredAtUtc"
            FROM patient_demographics AS p
            LEFT JOIN medicore_patient.medical_records AS mr
                ON mr."PatientId" = p."Id"
               AND mr."IsDeleted" = false
               AND mr."IsCurrent" = true
            """);

        var command = connection.CreateCommand();
        command.Parameters.AddWithValue("asOfDate", NpgsqlDbType.Date, asOfDate);

        if (filter.From.HasValue)
        {
            sql.AppendLine("   AND mr.\"AuthoredAtUtc\" >= @fromUtc");
            command.Parameters.AddWithValue(
                "fromUtc",
                NpgsqlDbType.TimestampTz,
                ToUtcStartOfDay(filter.From.Value));
        }

        if (filter.To.HasValue)
        {
            sql.AppendLine("   AND mr.\"AuthoredAtUtc\" < @toExclusiveUtc");
            command.Parameters.AddWithValue(
                "toExclusiveUtc",
                NpgsqlDbType.TimestampTz,
                ToUtcStartOfDay(filter.To.Value.AddDays(1)));
        }

        sql.AppendLine("WHERE 1 = 1");

        if (requestedAgeBand is not null)
        {
            sql.AppendLine("  AND p.age_band = @ageBand");
            command.Parameters.AddWithValue("ageBand", NpgsqlDbType.Varchar, requestedAgeBand);
        }

        if (!string.IsNullOrWhiteSpace(filter.Gender))
        {
            sql.AppendLine("  AND p.\"Gender\" = @gender");
            command.Parameters.AddWithValue(
                "gender",
                NpgsqlDbType.Varchar,
                DemographicsReportOptions.NormalizeGender(filter.Gender));
        }

        if (!string.IsNullOrWhiteSpace(filter.District))
        {
            sql.AppendLine("  AND lower(p.\"District\") = lower(@district)");
            command.Parameters.AddWithValue("district", NpgsqlDbType.Varchar, filter.District.Trim());
        }

        if (filter.From.HasValue || filter.To.HasValue)
        {
            sql.AppendLine("  AND mr.\"RecordId\" IS NOT NULL");
        }

        sql.AppendLine("ORDER BY p.\"District\", p.\"PatientNumber\", mr.\"AuthoredAtUtc\"");
        command.CommandText = sql.ToString();
        return command;
    }

    private static DemographicsReportSourceRow MapRow(NpgsqlDataReader reader) => new(
        PatientId: reader.GetGuid(0),
        PatientNumber: reader.GetString(1),
        Age: reader.GetInt32(2),
        AgeBand: reader.GetString(3),
        Gender: reader.GetString(4),
        District: reader.GetString(5),
        VisitRecordId: reader.IsDBNull(6) ? null : reader.GetGuid(6),
        VisitReference: reader.IsDBNull(7) ? null : reader.GetGuid(7),
        VisitAtUtc: reader.IsDBNull(8) ? null : reader.GetDateTime(8));

    private static DateTime ToUtcStartOfDay(DateOnly value) =>
        DateTime.SpecifyKind(value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
}
