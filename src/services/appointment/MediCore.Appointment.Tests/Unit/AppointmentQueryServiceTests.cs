using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Services;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Tests.Unit;

public sealed class AppointmentQueryServiceTests
{
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Now = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 9, 24);

    [Fact]
    public async Task Staff_see_who_booked_with_whom_and_when()
    {
        var appointment = Booked();
        var fixture = new Fixture(new AppointmentListing(appointment, "Dr Nimal Perera", "Cardiology"));

        var result = Assert.Single(await fixture.Service.ListAsync(DoctorId, Day, Day));

        Assert.Equal(appointment.AppointmentId, result.AppointmentId);
        Assert.Equal(PatientId, result.PatientId);
        Assert.Equal("PAT-000123", result.PatientNumber);
        Assert.Equal("Kamala Silva", result.PatientName);
        Assert.Equal(DoctorId, result.DoctorId);
        Assert.Equal("Dr Nimal Perera", result.DoctorName);
        Assert.Equal("Cardiology", result.Specialization);
        Assert.Equal(appointment.SlotId, result.SlotId);
        Assert.Equal(appointment.StartUtc, result.StartUtc);
        Assert.Equal(AppointmentStatus.Booked, result.Status);
    }

    [Fact]
    public async Task The_list_passes_the_doctor_and_dates_through_untouched()
    {
        var fixture = new Fixture();

        await fixture.Service.ListAsync(null, Day, Day.AddDays(6));

        Assert.Equal((null, Day, Day.AddDays(6)), fixture.Repository.LastListQuery);
    }

    [Fact]
    public async Task A_doctor_the_cache_never_heard_of_still_lists_with_no_name()
    {
        // Left join: the booking must not disappear from the clinic's view for want of a cache row.
        var fixture = new Fixture(new AppointmentListing(Booked(), null, null));

        var result = Assert.Single(await fixture.Service.ListAsync(DoctorId, Day, Day));

        Assert.Null(result.DoctorName);
    }

    [Fact]
    public async Task A_patient_reads_their_own_upcoming_bookings_as_of_now_with_a_cap()
    {
        var appointment = Booked();
        var fixture = new Fixture(new AppointmentListing(appointment, "Dr Nimal Perera", "Cardiology"));

        var result = Assert.Single(await fixture.Service.ListUpcomingForPatientAsync(PatientId));

        Assert.Equal(
            (PatientId, Now, AppointmentQueryService.UpcomingLimit),
            fixture.Repository.LastUpcomingQuery);
        Assert.Equal(appointment.AppointmentId, result.AppointmentId);
        Assert.Equal("Dr Nimal Perera", result.DoctorName);
        Assert.Equal(appointment.StartUtc, result.StartUtc);
        Assert.Equal(appointment.SlotDate, result.SlotDate);
    }

    private static AppointmentEntity Booked() => new()
    {
        SlotId = Guid.NewGuid(),
        PatientId = PatientId,
        PatientNumber = "PAT-000123",
        PatientName = "Kamala Silva",
        DoctorId = DoctorId,
        StartUtc = new DateTime(2026, 9, 24, 3, 30, 0, DateTimeKind.Utc),
        EndUtc = new DateTime(2026, 9, 24, 4, 0, 0, DateTimeKind.Utc),
        SlotDate = Day,
        DurationMinutes = 30,
        ServiceCode = ServiceCodes.GeneralConsultation,
        Status = AppointmentStatus.Booked
    };

    private sealed class Fixture
    {
        public Fixture(params AppointmentListing[] listings)
        {
            Repository = new FakeAppointmentRepository(listings);
            Service = new AppointmentQueryService(Repository, new FixedTimeProvider(Now));
        }

        public FakeAppointmentRepository Repository { get; }

        public AppointmentQueryService Service { get; }
    }

    private sealed class FakeAppointmentRepository : IAppointmentRepository
    {
        private readonly IReadOnlyList<AppointmentListing> _listings;

        public FakeAppointmentRepository(IReadOnlyList<AppointmentListing> listings)
        {
            _listings = listings;
        }

        public (Guid? DoctorId, DateOnly From, DateOnly To)? LastListQuery { get; private set; }

        public (Guid PatientId, DateTime NowUtc, int Limit)? LastUpcomingQuery { get; private set; }

        public Task<IReadOnlyList<AppointmentListing>> ListAsync(
            Guid? doctorId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            LastListQuery = (doctorId, from, to);
            return Task.FromResult(_listings);
        }

        public Task<IReadOnlyList<AppointmentListing>> ListUpcomingForPatientAsync(
            Guid patientId, DateTime nowUtc, int limit, CancellationToken cancellationToken = default)
        {
            LastUpcomingQuery = (patientId, nowUtc, limit);
            return Task.FromResult(_listings);
        }

        public Task AddAsync(AppointmentEntity appointment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Queries never write.");

        public Task<AppointmentEntity?> FindPatientOverlapAsync(
            Guid patientId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Queries never check overlaps.");

        public Task<AppointmentEntity?> GetByAppointmentIdAsync(
            Guid appointmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by the list queries.");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTime now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
