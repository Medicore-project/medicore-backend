using System.Data;
using System.Text;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace MediCore.Appointment.Infrastructure.Reporting;

/// <inheritdoc cref="IUtilisationReportQuery"/>
/// <remarks>
/// <para>
/// Raw ADO.NET on the context's own connection, as the module requires of report queries. Each
/// table is read once: <c>COUNT(*) FILTER (WHERE …)</c> gives every per-status count in a single
/// grouped pass instead of one query, or one <c>CASE</c> sum, per status.
/// </para>
/// <para>
/// Every value is a typed parameter — the filter ids and dates, and the status names too — so the
/// command text holds no data at all. Optional filters are appended as fixed clauses; nothing the
/// caller sends is ever concatenated into the SQL.
/// </para>
/// <para>
/// Both scans are served by existing indexes: <c>ix_appointments_date_doctor</c> and
/// <c>ix_slots_doctor_date_status</c>. Soft-deleted rows are excluded by hand, since raw SQL does not
/// see EF's query filters.
/// </para>
/// </remarks>
public sealed class UtilisationReportQuery : IUtilisationReportQuery
{
    /// <summary>
    /// Slot statuses that mean someone holds the time: booked, held for the waitlist, or booked and
    /// then stranded by a schedule change.
    /// </summary>
    internal static readonly string[] UsedSlotStatuses = [SlotStatus.Booked, SlotStatus.Offered, SlotStatus.Flagged];

    private readonly AppointmentDbContext _dbContext;

    public UtilisationReportQuery(AppointmentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UtilisationSourceRow>> QueryAsync(
        UtilisationQueryCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = BuildCommand(connection, criteria);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var rows = new List<UtilisationSourceRow>();
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

    internal static NpgsqlCommand BuildCommand(NpgsqlConnection connection, UtilisationQueryCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.From > criteria.To)
        {
            throw new ArgumentException("To date must be on or after from date.", nameof(criteria));
        }

        var sql = new StringBuilder(
            """
            WITH appointment_counts AS (
                SELECT a."DoctorId",
                       COUNT(*) FILTER (WHERE a."Status" = @completed) AS completed,
                       COUNT(*) FILTER (WHERE a."Status" = @noShow) AS no_show,
                       COUNT(*) FILTER (WHERE a."Status" = @cancelled) AS cancelled,
                       COUNT(*) FILTER (WHERE a."Status" = @booked) AS booked
                FROM medicore_appointment.appointments AS a
                WHERE a."IsDeleted" = false
                  AND a."SlotDate" BETWEEN @from AND @to
                GROUP BY a."DoctorId"
            ),
            slot_counts AS (
                SELECT s."DoctorId",
                       COUNT(*) FILTER (WHERE s."Status" <> @blocked) AS bookable_slots,
                       COUNT(*) FILTER (WHERE s."Status" = ANY(@usedSlotStatuses)) AS used_slots
                FROM medicore_appointment.slots AS s
                WHERE s."IsDeleted" = false
                  AND s."SlotDate" BETWEEN @from AND @to
                GROUP BY s."DoctorId"
            )
            SELECT d."DoctorId",
                   d."FullName",
                   d."Specialization",
                   d."DepartmentId",
                   d."IsActive",
                   COALESCE(ac.completed, 0) AS completed,
                   COALESCE(ac.no_show, 0) AS no_show,
                   COALESCE(ac.cancelled, 0) AS cancelled,
                   COALESCE(ac.booked, 0) AS booked,
                   COALESCE(sc.bookable_slots, 0) AS bookable_slots,
                   COALESCE(sc.used_slots, 0) AS used_slots
            FROM medicore_appointment.cached_doctors AS d
            LEFT JOIN appointment_counts AS ac ON ac."DoctorId" = d."DoctorId"
            LEFT JOIN slot_counts AS sc ON sc."DoctorId" = d."DoctorId"
            WHERE d."IsDeleted" = false
            """);
        sql.AppendLine();

        var command = connection.CreateCommand();
        command.Parameters.AddWithValue("from", NpgsqlDbType.Date, criteria.From);
        command.Parameters.AddWithValue("to", NpgsqlDbType.Date, criteria.To);
        command.Parameters.AddWithValue("completed", NpgsqlDbType.Varchar, AppointmentStatus.Completed);
        command.Parameters.AddWithValue("noShow", NpgsqlDbType.Varchar, AppointmentStatus.NoShow);
        command.Parameters.AddWithValue("cancelled", NpgsqlDbType.Varchar, AppointmentStatus.Cancelled);
        command.Parameters.AddWithValue("booked", NpgsqlDbType.Varchar, AppointmentStatus.Booked);
        command.Parameters.AddWithValue("blocked", NpgsqlDbType.Varchar, SlotStatus.Blocked);
        command.Parameters.AddWithValue(
            "usedSlotStatuses",
            NpgsqlDbType.Array | NpgsqlDbType.Varchar,
            UsedSlotStatuses);

        if (criteria.DoctorId.HasValue)
        {
            sql.AppendLine("  AND d.\"DoctorId\" = @doctorId");
            command.Parameters.AddWithValue("doctorId", NpgsqlDbType.Uuid, criteria.DoctorId.Value);
        }

        if (criteria.DepartmentId.HasValue)
        {
            sql.AppendLine("  AND d.\"DepartmentId\" = @departmentId");
            command.Parameters.AddWithValue("departmentId", NpgsqlDbType.Uuid, criteria.DepartmentId.Value);
        }

        // AC1's "all doctors": every active one, even with nothing booked. A doctor who has since
        // left still appears for a period in which they had appointments or slots.
        sql.AppendLine("  AND (d.\"IsActive\" OR ac.\"DoctorId\" IS NOT NULL OR sc.\"DoctorId\" IS NOT NULL)");
        sql.AppendLine("ORDER BY d.\"FullName\", d.\"DoctorId\"");

        command.CommandText = sql.ToString();
        return command;
    }

    private static UtilisationSourceRow MapRow(NpgsqlDataReader reader) => new(
        DoctorId: reader.GetGuid(0),
        FullName: reader.GetString(1),
        Specialization: reader.GetString(2),
        DepartmentId: reader.GetGuid(3),
        IsActive: reader.GetBoolean(4),
        Completed: ToInt(reader, 5),
        NoShow: ToInt(reader, 6),
        Cancelled: ToInt(reader, 7),
        Booked: ToInt(reader, 8),
        BookableSlots: ToInt(reader, 9),
        UsedSlots: ToInt(reader, 10));

    /// <summary>
    /// <c>COUNT</c> is a <c>bigint</c>. A doctor's appointments in at most 366 days fit an
    /// <c>int</c> many times over; <c>checked</c> says so loudly if that ever stops being true.
    /// </summary>
    private static int ToInt(NpgsqlDataReader reader, int ordinal) => checked((int)reader.GetInt64(ordinal));
}
