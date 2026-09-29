using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Infrastructure.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>
/// SCRUM-38: the SQL the utilisation query builds. No database — the command is only built, never
/// run. Assertions are on single-line fragments so they hold whatever line endings the SQL has.
/// </summary>
public sealed class UtilisationReportQueryTests
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DepartmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void Each_count_is_a_filter_aggregate_in_one_pass_per_table()
    {
        using var connection = Connection();
        using var command = UtilisationReportQuery.BuildCommand(connection, Criteria());

        var sql = command.CommandText;
        Assert.Contains("COUNT(*) FILTER (WHERE a.\"Status\" = @completed) AS completed", sql);
        Assert.Contains("COUNT(*) FILTER (WHERE a.\"Status\" = @noShow) AS no_show", sql);
        Assert.Contains("COUNT(*) FILTER (WHERE a.\"Status\" = @cancelled) AS cancelled", sql);
        Assert.Contains("COUNT(*) FILTER (WHERE a.\"Status\" = @booked) AS booked", sql);
        Assert.Contains("COUNT(*) FILTER (WHERE s.\"Status\" <> @blocked) AS bookable_slots", sql);
        Assert.Contains("COUNT(*) FILTER (WHERE s.\"Status\" = ANY(@usedSlotStatuses)) AS used_slots", sql);

        // One grouped read of each table, joined to the doctors once.
        Assert.Single(Occurrences(sql, "FROM medicore_appointment.appointments AS a"));
        Assert.Single(Occurrences(sql, "FROM medicore_appointment.slots AS s"));
        Assert.Single(Occurrences(sql, "FROM medicore_appointment.cached_doctors AS d"));
        Assert.Equal(2, Occurrences(sql, "GROUP BY").Count);
    }

    [Fact]
    public void Soft_deleted_rows_are_excluded_by_hand_and_the_period_bounds_both_tables()
    {
        using var connection = Connection();
        using var command = UtilisationReportQuery.BuildCommand(connection, Criteria());

        var sql = command.CommandText;
        Assert.Contains("a.\"IsDeleted\" = false", sql);
        Assert.Contains("s.\"IsDeleted\" = false", sql);
        Assert.Contains("d.\"IsDeleted\" = false", sql);
        Assert.Contains("a.\"SlotDate\" BETWEEN @from AND @to", sql);
        Assert.Contains("s.\"SlotDate\" BETWEEN @from AND @to", sql);
    }

    [Fact]
    public void With_no_filters_every_active_doctor_and_any_with_activity_is_included()
    {
        using var connection = Connection();
        using var command = UtilisationReportQuery.BuildCommand(connection, Criteria());

        var sql = command.CommandText;
        Assert.Contains("LEFT JOIN appointment_counts AS ac", sql);
        Assert.Contains("LEFT JOIN slot_counts AS sc", sql);
        Assert.Contains("AND (d.\"IsActive\" OR ac.\"DoctorId\" IS NOT NULL OR sc.\"DoctorId\" IS NOT NULL)", sql);
        Assert.DoesNotContain("@doctorId", sql);
        Assert.DoesNotContain("@departmentId", sql);
        Assert.Equal(8, command.Parameters.Count);
    }

    [Fact]
    public void The_period_and_every_status_are_typed_parameters()
    {
        using var connection = Connection();
        using var command = UtilisationReportQuery.BuildCommand(connection, Criteria());

        AssertParameter(command, "from", NpgsqlDbType.Date, From);
        AssertParameter(command, "to", NpgsqlDbType.Date, To);
        AssertParameter(command, "completed", NpgsqlDbType.Varchar, AppointmentStatus.Completed);
        AssertParameter(command, "noShow", NpgsqlDbType.Varchar, AppointmentStatus.NoShow);
        AssertParameter(command, "cancelled", NpgsqlDbType.Varchar, AppointmentStatus.Cancelled);
        AssertParameter(command, "booked", NpgsqlDbType.Varchar, AppointmentStatus.Booked);
        AssertParameter(command, "blocked", NpgsqlDbType.Varchar, SlotStatus.Blocked);

        var used = Parameter(command, "usedSlotStatuses");
        Assert.Equal(NpgsqlDbType.Array | NpgsqlDbType.Varchar, used.NpgsqlDbType);
        Assert.Equal(new[] { SlotStatus.Booked, SlotStatus.Offered, SlotStatus.Flagged }, used.Value);
    }

    [Fact]
    public void A_doctor_filter_adds_one_clause_and_one_parameter()
    {
        using var connection = Connection();
        using var command = UtilisationReportQuery.BuildCommand(connection, Criteria(doctorId: DoctorId));

        Assert.Contains("AND d.\"DoctorId\" = @doctorId", command.CommandText);
        Assert.DoesNotContain("@departmentId", command.CommandText);
        Assert.Equal(9, command.Parameters.Count);
        AssertParameter(command, "doctorId", NpgsqlDbType.Uuid, DoctorId);
    }

    [Fact]
    public void A_department_filter_adds_one_clause_and_one_parameter()
    {
        using var connection = Connection();
        using var command = UtilisationReportQuery.BuildCommand(connection, Criteria(departmentId: DepartmentId));

        Assert.Contains("AND d.\"DepartmentId\" = @departmentId", command.CommandText);
        Assert.DoesNotContain("@doctorId", command.CommandText);
        Assert.Equal(9, command.Parameters.Count);
        AssertParameter(command, "departmentId", NpgsqlDbType.Uuid, DepartmentId);
    }

    [Fact]
    public void Every_filter_together_is_all_parameters_and_no_value_reaches_the_sql_text()
    {
        // AC4: whatever the combination, the command text carries no data — no id, no date, no
        // status name.
        using var connection = Connection();
        using var command = UtilisationReportQuery.BuildCommand(
            connection, Criteria(doctorId: DoctorId, departmentId: DepartmentId));

        var sql = command.CommandText;
        Assert.Equal(10, command.Parameters.Count);
        Assert.DoesNotContain(DoctorId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DepartmentId.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2026", sql);
        foreach (var status in new[] { "'Completed'", "'NoShow'", "'Cancelled'", "'Booked'", "'Blocked'", "'Offered'", "'Flagged'" })
        {
            Assert.DoesNotContain(status, sql);
        }
    }

    [Fact]
    public void Rows_are_ordered_by_doctor_name()
    {
        using var connection = Connection();
        using var command = UtilisationReportQuery.BuildCommand(connection, Criteria());

        Assert.Contains("ORDER BY d.\"FullName\", d.\"DoctorId\"", command.CommandText);
    }

    [Fact]
    public void A_backwards_period_is_refused()
    {
        using var connection = Connection();

        Assert.Throws<ArgumentException>(() =>
            UtilisationReportQuery.BuildCommand(connection, new UtilisationQueryCriteria(null, null, To, From)));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static UtilisationQueryCriteria Criteria(Guid? doctorId = null, Guid? departmentId = null) =>
        new(doctorId, departmentId, From, To);

    private static NpgsqlConnection Connection() => new(
        "Host=localhost;Database=unused;Username=unused;Password=unused");

    private static NpgsqlParameter Parameter(NpgsqlCommand command, string name) =>
        Assert.Single(command.Parameters.Cast<NpgsqlParameter>(), p => p.ParameterName == name);

    private static void AssertParameter(NpgsqlCommand command, string name, NpgsqlDbType type, object value)
    {
        var parameter = Parameter(command, name);
        Assert.Equal(type, parameter.NpgsqlDbType);
        Assert.Equal(value, parameter.Value);
    }

    private static List<int> Occurrences(string text, string fragment)
    {
        var positions = new List<int>();
        for (var index = text.IndexOf(fragment, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal))
        {
            positions.Add(index);
        }

        return positions;
    }
}
