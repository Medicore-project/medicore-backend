using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Infrastructure.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace MediCore.Patient.Tests.Unit;

public sealed class DemographicsReportQueryTests
{
    private static readonly DateOnly AsOfDate = new(2026, 9, 14);

    [Fact]
    public void No_filter_query_includes_every_active_patient_and_only_current_active_visits()
    {
        using var connection = Connection();
        using var command = DemographicsReportQuery.BuildCommand(
            connection,
            new DemographicsReportFilter(),
            AsOfDate);

        Assert.Contains("p.\"IsDeleted\" = false", command.CommandText);
        Assert.Contains("mr.\"IsDeleted\" = false", command.CommandText);
        Assert.Contains("mr.\"IsCurrent\" = true", command.CommandText);
        Assert.Contains("LEFT JOIN medicore_patient.medical_records", command.CommandText);
        Assert.DoesNotContain("mr.\"RecordId\" IS NOT NULL", command.CommandText);
        Assert.Contains("EXTRACT(YEAR FROM AGE(@asOfDate", command.CommandText);
        Assert.Contains("END AS age_band", command.CommandText);

        var parameter = Assert.Single(command.Parameters.Cast<NpgsqlParameter>());
        Assert.Equal("asOfDate", parameter.ParameterName);
        Assert.Equal(NpgsqlDbType.Date, parameter.NpgsqlDbType);
        Assert.Equal(AsOfDate, parameter.Value);
    }

    [Fact]
    public void Every_filter_value_is_a_typed_parameter_and_never_part_of_sql_text()
    {
        const string district = "Colombo' OR 1=1 --";
        var filter = new DemographicsReportFilter(
            DemographicsReportOptions.Age18To34,
            " female ",
            $"  {district}  ",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31));
        using var connection = Connection();
        using var command = DemographicsReportQuery.BuildCommand(connection, filter, AsOfDate);

        Assert.DoesNotContain(district, command.CommandText);
        Assert.Equal(6, command.Parameters.Count);
        AssertParameter(command, "ageBand", NpgsqlDbType.Varchar, DemographicsReportOptions.Age18To34);
        AssertParameter(command, "gender", NpgsqlDbType.Varchar, "Female");
        AssertParameter(command, "district", NpgsqlDbType.Varchar, district);
        AssertParameter(
            command,
            "fromUtc",
            NpgsqlDbType.TimestampTz,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        AssertParameter(
            command,
            "toExclusiveUtc",
            NpgsqlDbType.TimestampTz,
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Contains("mr.\"RecordId\" IS NOT NULL", command.CommandText);
    }

    [Theory]
    [MemberData(nameof(FilterCombinations))]
    public void Filters_are_composable_in_every_combination(
        bool includeAge,
        bool includeGender,
        bool includeDistrict,
        bool includeDateRange)
    {
        var filter = new DemographicsReportFilter(
            includeAge ? DemographicsReportOptions.Age65Plus : null,
            includeGender ? "Other" : null,
            includeDistrict ? "Galle" : null,
            includeDateRange ? new DateOnly(2026, 1, 1) : null,
            includeDateRange ? new DateOnly(2026, 3, 31) : null);
        using var connection = Connection();
        using var command = DemographicsReportQuery.BuildCommand(connection, filter, AsOfDate);

        Assert.Equal(includeAge, command.Parameters.Contains("ageBand"));
        Assert.Equal(includeGender, command.Parameters.Contains("gender"));
        Assert.Equal(includeDistrict, command.Parameters.Contains("district"));
        Assert.Equal(includeDateRange, command.Parameters.Contains("fromUtc"));
        Assert.Equal(includeDateRange, command.Parameters.Contains("toExclusiveUtc"));
        Assert.Equal(includeDateRange, command.CommandText.Contains("mr.\"RecordId\" IS NOT NULL"));
    }

    [Fact]
    public void Unsupported_age_band_is_rejected_before_command_execution()
    {
        using var connection = Connection();
        var filter = new DemographicsReportFilter("18-35", null, null, null, null);

        Assert.Throws<ArgumentException>(() =>
            DemographicsReportQuery.BuildCommand(connection, filter, AsOfDate));
    }

    [Fact]
    public void Invalid_date_boundary_is_rejected_before_command_execution()
    {
        using var connection = Connection();
        var reversed = new DemographicsReportFilter(
            null,
            null,
            null,
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 1, 31));
        var maximumEndDate = new DemographicsReportFilter(null, null, null, null, DateOnly.MaxValue);

        Assert.Throws<ArgumentException>(() =>
            DemographicsReportQuery.BuildCommand(connection, reversed, AsOfDate));
        Assert.Throws<ArgumentException>(() =>
            DemographicsReportQuery.BuildCommand(connection, maximumEndDate, AsOfDate));
    }

    public static IEnumerable<object[]> FilterCombinations()
    {
        for (var mask = 0; mask < 16; mask++)
        {
            yield return
            [
                (mask & 1) != 0,
                (mask & 2) != 0,
                (mask & 4) != 0,
                (mask & 8) != 0
            ];
        }
    }

    private static NpgsqlConnection Connection() => new(
        "Host=localhost;Database=unused;Username=unused;Password=unused");

    private static void AssertParameter(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object expectedValue)
    {
        var parameter = command.Parameters[name];
        Assert.Equal(type, parameter.NpgsqlDbType);
        Assert.Equal(expectedValue, parameter.Value);
    }
}
