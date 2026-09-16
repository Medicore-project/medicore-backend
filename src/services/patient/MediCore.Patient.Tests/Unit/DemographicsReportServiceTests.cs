using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Application.Services;

namespace MediCore.Patient.Tests.Unit;

public sealed class DemographicsReportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Empty_query_returns_zero_summary_and_complete_fixed_breakdowns()
    {
        var query = new StubQuery([]);
        var service = new DemographicsReportService(query, new FixedTimeProvider(Now));

        var result = await service.GenerateAsync(new DemographicsReportFilter());

        Assert.Equal(Now.UtcDateTime, result.GeneratedAtUtc);
        Assert.Equal(0, result.TotalPatients);
        Assert.Equal(0, result.TotalVisits);
        Assert.Equal(DemographicsReportOptions.AgeBands, result.AgeBands.Select(item => item.Label));
        Assert.Equal(DemographicsReportOptions.Genders, result.Genders.Select(item => item.Label));
        Assert.All(result.AgeBands.Concat(result.Genders), item =>
        {
            Assert.Equal(0, item.PatientCount);
            Assert.Equal(0, item.VisitCount);
        });
        Assert.Empty(result.Districts);
        Assert.Empty(result.VisitHistory);
        Assert.Empty(result.Patients);
        Assert.Equal(1, query.CallCount);
    }

    [Fact]
    public async Task Aggregates_distinct_patients_visits_and_monthly_history()
    {
        var patientOne = Guid.NewGuid();
        var patientTwo = Guid.NewGuid();
        var visitOne = Guid.NewGuid();
        var visitTwo = Guid.NewGuid();
        var rows = new[]
        {
            Row(patientOne, "PAT-000002", 31, "18-34", "Female", "Colombo", visitOne, new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc)),
            Row(patientOne, "PAT-000002", 31, "18-34", "Female", "Colombo", visitOne, new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc)),
            Row(patientOne, "PAT-000002", 31, "18-34", "Female", "Colombo", visitTwo, new DateTime(2026, 2, 4, 9, 0, 0, DateTimeKind.Utc)),
            Row(patientTwo, "PAT-000001", 67, "65+", "Male", "Galle", null, null)
        };
        var query = new StubQuery(rows);
        var service = new DemographicsReportService(query, new FixedTimeProvider(Now));

        var result = await service.GenerateAsync(new DemographicsReportFilter());

        Assert.Equal(2, result.TotalPatients);
        Assert.Equal(2, result.TotalVisits);
        Assert.Equal(1, result.PatientsWithVisits);
        Assert.Equal(1, result.PatientsWithoutVisits);
        Assert.Equal(["PAT-000001", "PAT-000002"], result.Patients.Select(item => item.PatientNumber));
        Assert.Equal(2, result.Patients.Single(item => item.PatientId == patientOne).VisitCount);
        Assert.Equal(new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc), result.Patients.Single(item => item.PatientId == patientOne).FirstVisitAtUtc);
        Assert.Equal(new DateTime(2026, 2, 4, 9, 0, 0, DateTimeKind.Utc), result.Patients.Single(item => item.PatientId == patientOne).LatestVisitAtUtc);
        Assert.Equal(["2026-01", "2026-02"], result.VisitHistory.Select(item => item.Period));
        Assert.All(result.VisitHistory, item => Assert.Equal(1, item.VisitCount));
        Assert.Equal(2, result.Districts.Count);
        Assert.Equal(1, result.AgeBands.Single(item => item.Label == "18-34").PatientCount);
        Assert.Equal(2, result.AgeBands.Single(item => item.Label == "18-34").VisitCount);
    }

    [Fact]
    public async Task Preserves_normalized_filters_and_passes_original_filter_to_query_once()
    {
        var filter = new DemographicsReportFilter(
            " 65+ ",
            " prefer not to say ",
            " Colombo ",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 6, 30));
        var query = new StubQuery([]);
        var service = new DemographicsReportService(query, new FixedTimeProvider(Now));

        var result = await service.GenerateAsync(filter);

        Assert.Same(filter, query.LastFilter);
        Assert.Equal(1, query.CallCount);
        Assert.Equal("65+", result.AppliedFilters.AgeBand);
        Assert.Equal("PreferNotToSay", result.AppliedFilters.Gender);
        Assert.Equal("Colombo", result.AppliedFilters.District);
        Assert.Equal(filter.From, result.AppliedFilters.From);
        Assert.Equal(filter.To, result.AppliedFilters.To);
    }

    private static DemographicsReportSourceRow Row(
        Guid patientId,
        string patientNumber,
        int age,
        string ageBand,
        string gender,
        string district,
        Guid? visitRecordId,
        DateTime? visitAtUtc) => new(
        patientId,
        patientNumber,
        age,
        ageBand,
        gender,
        district,
        visitRecordId,
        visitRecordId.HasValue ? Guid.NewGuid() : null,
        visitAtUtc);

    private sealed class StubQuery : IDemographicsReportQuery
    {
        private readonly IReadOnlyList<DemographicsReportSourceRow> _rows;

        public StubQuery(IReadOnlyList<DemographicsReportSourceRow> rows)
        {
            _rows = rows;
        }

        public int CallCount { get; private set; }
        public DemographicsReportFilter? LastFilter { get; private set; }

        public Task<IReadOnlyList<DemographicsReportSourceRow>> QueryAsync(
            DemographicsReportFilter filter,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastFilter = filter;
            return Task.FromResult(_rows);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
