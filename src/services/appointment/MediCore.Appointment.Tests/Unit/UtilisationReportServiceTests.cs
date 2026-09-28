using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Services;

namespace MediCore.Appointment.Tests.Unit;

/// <summary>SCRUM-38: resolving the report period and working out totals and rates.</summary>
public sealed class UtilisationReportServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);   // 13:30 in Colombo
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherDoctorId = Guid.Parse("11111111-1111-1111-1111-222222222222");
    private static readonly Guid DepartmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // ── The period (AC1) ──────────────────────────────────────────────────────

    [Fact]
    public async Task With_no_dates_the_report_covers_the_current_colombo_month()
    {
        var fixture = new Fixture(Now);

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter());

        var criteria = Assert.Single(fixture.Query.Calls);
        Assert.Equal(new DateOnly(2026, 9, 1), criteria.From);
        Assert.Equal(new DateOnly(2026, 9, 30), criteria.To);
        Assert.Equal(new DateOnly(2026, 9, 1), report.AppliedFilters.From);
        Assert.Equal(new DateOnly(2026, 9, 30), report.AppliedFilters.To);
        Assert.True(report.AppliedFilters.IsDefaultPeriod);
    }

    [Fact]
    public async Task The_current_month_is_colombos_even_when_utc_is_still_in_the_previous_one()
    {
        // 30 Sep 19:00 UTC is 1 Oct 00:30 in Colombo, so the default month is already October.
        var fixture = new Fixture(new DateTime(2026, 9, 30, 19, 0, 0, DateTimeKind.Utc));

        await fixture.Service.GenerateAsync(new UtilisationReportFilter());

        var criteria = Assert.Single(fixture.Query.Calls);
        Assert.Equal(new DateOnly(2026, 10, 1), criteria.From);
        Assert.Equal(new DateOnly(2026, 10, 31), criteria.To);
    }

    [Fact]
    public async Task A_given_range_is_used_as_it_is()
    {
        var fixture = new Fixture(Now);

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter
        {
            From = new DateOnly(2026, 7, 15),
            To = new DateOnly(2026, 8, 20)
        });

        var criteria = Assert.Single(fixture.Query.Calls);
        Assert.Equal(new DateOnly(2026, 7, 15), criteria.From);
        Assert.Equal(new DateOnly(2026, 8, 20), criteria.To);
        Assert.False(report.AppliedFilters.IsDefaultPeriod);
    }

    [Fact]
    public async Task Only_a_start_date_runs_to_the_end_of_its_month()
    {
        var fixture = new Fixture(Now);

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter { From = new DateOnly(2028, 2, 10) });

        var criteria = Assert.Single(fixture.Query.Calls);
        Assert.Equal(new DateOnly(2028, 2, 10), criteria.From);
        Assert.Equal(new DateOnly(2028, 2, 29), criteria.To); // a leap year
        Assert.False(report.AppliedFilters.IsDefaultPeriod);
    }

    [Fact]
    public async Task Only_an_end_date_starts_at_the_beginning_of_its_month()
    {
        var fixture = new Fixture(Now);

        await fixture.Service.GenerateAsync(new UtilisationReportFilter { To = new DateOnly(2026, 8, 20) });

        var criteria = Assert.Single(fixture.Query.Calls);
        Assert.Equal(new DateOnly(2026, 8, 1), criteria.From);
        Assert.Equal(new DateOnly(2026, 8, 20), criteria.To);
    }

    [Fact]
    public async Task A_backwards_range_that_slipped_past_validation_is_refused_before_the_query()
    {
        var fixture = new Fixture(Now);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.GenerateAsync(new UtilisationReportFilter
        {
            From = new DateOnly(2026, 9, 10),
            To = new DateOnly(2026, 9, 9)
        }));

        Assert.Empty(fixture.Query.Calls);
    }

    // ── Filters (AC2) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Doctor_and_department_reach_the_query_and_the_applied_filters()
    {
        var fixture = new Fixture(Now);

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter
        {
            DoctorId = DoctorId,
            DepartmentId = DepartmentId,
            DepartmentName = "  Cardiology  "
        });

        var criteria = Assert.Single(fixture.Query.Calls);
        Assert.Equal(DoctorId, criteria.DoctorId);
        Assert.Equal(DepartmentId, criteria.DepartmentId);
        Assert.Equal(DoctorId, report.AppliedFilters.DoctorId);
        Assert.Equal(DepartmentId, report.AppliedFilters.DepartmentId);
        Assert.Equal("Cardiology", report.AppliedFilters.DepartmentName);
    }

    [Fact]
    public async Task A_department_name_without_a_department_is_dropped()
    {
        // Otherwise an export would claim a department filter that was never applied.
        var fixture = new Fixture(Now);

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter { DepartmentName = "Cardiology" });

        Assert.Null(report.AppliedFilters.DepartmentName);
        Assert.Null(Assert.Single(fixture.Query.Calls).DepartmentId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_blank_department_name_is_no_name(string? name)
    {
        var fixture = new Fixture(Now);

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter
        {
            DepartmentId = DepartmentId,
            DepartmentName = name
        });

        Assert.Null(report.AppliedFilters.DepartmentName);
    }

    // ── Counts and rates (AC3) ────────────────────────────────────────────────

    [Fact]
    public async Task Each_doctor_gets_a_total_and_rates_from_their_counts()
    {
        var fixture = new Fixture(Now);
        fixture.Query.Rows.Add(Row(DoctorId, "Dr. Perera", completed: 7, noShow: 3, cancelled: 4, booked: 5, bookable: 20, used: 15));

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter());

        var doctor = Assert.Single(report.Doctors);
        Assert.Equal(7, doctor.Completed);
        Assert.Equal(3, doctor.NoShow);
        Assert.Equal(4, doctor.Cancelled);
        Assert.Equal(5, doctor.Booked);
        Assert.Equal(15, doctor.Total);          // cancelled appointments are not counted
        Assert.Equal(0.3m, doctor.NoShowRate);   // 3 of 10 outcomes; the 5 still booked are left out
        Assert.Equal(0.75m, doctor.FillRate);    // 15 of 20 bookable slots
        Assert.Equal(20, doctor.BookableSlots);
        Assert.Equal(15, doctor.UsedSlots);
    }

    [Fact]
    public async Task Rates_are_rounded_to_four_places()
    {
        var fixture = new Fixture(Now);
        fixture.Query.Rows.Add(Row(DoctorId, "Dr. Perera", completed: 2, noShow: 1, bookable: 3, used: 2));

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter());

        var doctor = Assert.Single(report.Doctors);
        Assert.Equal(0.3333m, doctor.NoShowRate);
        Assert.Equal(0.6667m, doctor.FillRate);
    }

    [Fact]
    public async Task A_doctor_with_nothing_to_divide_by_has_no_rate_rather_than_zero()
    {
        // Only future bookings and no generated slots: "no data yet", not "never misses".
        var fixture = new Fixture(Now);
        fixture.Query.Rows.Add(Row(DoctorId, "Dr. Perera", booked: 4));

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter());

        var doctor = Assert.Single(report.Doctors);
        Assert.Equal(4, doctor.Total);
        Assert.Null(doctor.NoShowRate);
        Assert.Null(doctor.FillRate);
    }

    [Fact]
    public async Task A_fully_used_doctor_reads_as_a_fill_of_one()
    {
        var fixture = new Fixture(Now);
        fixture.Query.Rows.Add(Row(DoctorId, "Dr. Perera", completed: 10, bookable: 10, used: 10));

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter());

        Assert.Equal(1m, Assert.Single(report.Doctors).FillRate);
    }

    [Fact]
    public async Task Totals_come_from_the_summed_counts_not_an_average_of_rates()
    {
        // Doctor A: 1 no-show of 2 outcomes (50%). Doctor B: 1 of 10 (10%). The average of rates
        // would be 30%; the clinic's real rate is 2 of 12.
        var fixture = new Fixture(Now);
        fixture.Query.Rows.Add(Row(DoctorId, "Dr. A", completed: 1, noShow: 1, cancelled: 1, booked: 2, bookable: 5, used: 4));
        fixture.Query.Rows.Add(Row(OtherDoctorId, "Dr. B", completed: 9, noShow: 1, booked: 0, bookable: 15, used: 10));

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter());

        var totals = report.Totals;
        Assert.Equal(2, totals.Doctors);
        Assert.Equal(10, totals.Completed);
        Assert.Equal(2, totals.NoShow);
        Assert.Equal(1, totals.Cancelled);
        Assert.Equal(2, totals.Booked);
        Assert.Equal(14, totals.Total);
        Assert.Equal(0.1667m, totals.NoShowRate);
        Assert.Equal(20, totals.BookableSlots);
        Assert.Equal(14, totals.UsedSlots);
        Assert.Equal(0.7m, totals.FillRate);
    }

    [Fact]
    public async Task No_doctors_gives_zero_totals_and_no_rates()
    {
        var fixture = new Fixture(Now);

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter());

        Assert.Empty(report.Doctors);
        Assert.Equal(0, report.Totals.Doctors);
        Assert.Equal(0, report.Totals.Total);
        Assert.Null(report.Totals.NoShowRate);
        Assert.Null(report.Totals.FillRate);
    }

    [Fact]
    public async Task Doctors_are_listed_by_name_and_keep_their_details()
    {
        var fixture = new Fixture(Now);
        fixture.Query.Rows.Add(Row(DoctorId, "dr. Silva", isActive: false));
        fixture.Query.Rows.Add(Row(OtherDoctorId, "Dr. Fernando"));

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter());

        Assert.Equal(["Dr. Fernando", "dr. Silva"], report.Doctors.Select(d => d.DoctorName));
        var silva = report.Doctors[1];
        Assert.Equal(DoctorId, silva.DoctorId);
        Assert.Equal("General Medicine", silva.Specialization);
        Assert.Equal(DepartmentId, silva.DepartmentId);
        Assert.False(silva.IsActive);
    }

    [Fact]
    public async Task The_report_is_stamped_with_the_time_it_was_generated()
    {
        var fixture = new Fixture(Now);

        var report = await fixture.Service.GenerateAsync(new UtilisationReportFilter());

        Assert.Equal(Now, report.GeneratedAtUtc);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static UtilisationSourceRow Row(
        Guid doctorId,
        string name,
        int completed = 0,
        int noShow = 0,
        int cancelled = 0,
        int booked = 0,
        int bookable = 0,
        int used = 0,
        bool isActive = true) =>
        new(doctorId, name, "General Medicine", DepartmentId, isActive, completed, noShow, cancelled, booked, bookable, used);

    private sealed class Fixture
    {
        public Fixture(DateTime utcNow)
        {
            Query = new FakeUtilisationReportQuery();
            Service = new UtilisationReportService(Query, new FixedTimeProvider(utcNow));
        }

        public FakeUtilisationReportQuery Query { get; }

        public UtilisationReportService Service { get; }
    }

    private sealed class FakeUtilisationReportQuery : IUtilisationReportQuery
    {
        public List<UtilisationSourceRow> Rows { get; } = [];

        public List<UtilisationQueryCriteria> Calls { get; } = [];

        public Task<IReadOnlyList<UtilisationSourceRow>> QueryAsync(
            UtilisationQueryCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(criteria);
            return Task.FromResult<IReadOnlyList<UtilisationSourceRow>>(Rows.ToArray());
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
