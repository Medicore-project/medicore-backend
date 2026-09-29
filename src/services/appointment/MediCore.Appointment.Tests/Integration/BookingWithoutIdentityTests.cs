using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using MediCore.Appointment.Infrastructure.Messaging;
using MediCore.Appointment.Infrastructure.Persistence;
using MediCore.Contracts.Events;
using MediCore.Contracts.Events.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Appointment.Tests.Integration;

/// <summary>
/// SCRUM-33 [QA]: booking succeeds with Identity stopped. "Booking" here is everything that exists
/// before SCRUM-34 adds appointments: pick a doctor, give them a schedule, list their free slots,
/// and let them request leave.
/// </summary>
/// <remarks>
/// Needs the local Postgres (<c>docker compose up -d postgres</c>). Each test uses a fresh doctor
/// id and deletes everything it wrote, so it is safe against a shared development database.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class BookingWithoutIdentityTests : IClassFixture<AppointmentApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions IdentityJson = new();

    private readonly AppointmentApiFactory _factory;
    private readonly Guid _doctorId = Guid.NewGuid();
    private readonly string _specialization;
    private readonly List<Guid> _messageIds = [];

    public BookingWithoutIdentityTests(AppointmentApiFactory factory)
    {
        _factory = factory;

        // Unique per test, so the listing can be narrowed to this doctor in a shared database.
        _specialization = $"Integration-{_doctorId:N}"[..32];
    }

    [Fact]
    public async Task Booking_works_from_cached_doctor_data_with_no_identity_service()
    {
        // The host really has no Kafka consumer — the cache is fed only by the handler call below.
        Assert.Empty(_factory.Services.GetServices<IHostedService>().OfType<StaffEventsConsumer>());
        // And no outbox dispatcher, so the publisher registration really does sit inside the same
        // blank-bootstrap-servers guard. If this fails the host will not start without a broker.
        Assert.Empty(_factory.Services.GetServices<IHostedService>().OfType<OutboxProcessor>());

        await PublishAsync(DoctorUpdated("Nimal Perera", isActive: true));
        var receptionist = _factory.CreateClientAs("Receptionist");

        // Pick a doctor — from the cache, not Identity.
        var doctors = await receptionist.GetFromJsonAsync<List<DoctorResponse>>(
            $"/api/doctors?specialization={_specialization}");
        var doctor = Assert.Single(doctors!);
        Assert.Equal(_doctorId, doctor.DoctorId);
        Assert.Equal("Nimal Perera", doctor.FullName);

        // Give them working hours.
        var schedule = await receptionist.PostAsJsonAsync("/api/schedules", ScheduleRequest(daysAhead: 1));
        Assert.Equal(HttpStatusCode.Created, schedule.StatusCode);

        // See what can be booked.
        var slots = await receptionist.GetFromJsonAsync<List<SlotResponse>>(
            $"/api/slots/available?doctorId={_doctorId}");
        Assert.NotEmpty(slots!);
        Assert.All(slots!, slot => Assert.Equal(_doctorId, slot.DoctorId));

        // The doctor can request leave.
        var doctorClient = _factory.CreateClientAs("Doctor", _doctorId);
        var leave = await doctorClient.PostAsJsonAsync("/api/doctor-leaves", LeaveRequest());
        Assert.Equal(HttpStatusCode.Created, leave.StatusCode);

        // AC1: a later staff.updated refreshes name and specialization.
        await PublishAsync(DoctorUpdated("Nimal Perera-Silva", isActive: true, specialization: "Neurology"));
        var refreshed = await receptionist.GetFromJsonAsync<DoctorResponse>($"/api/doctors/{_doctorId}");
        Assert.Equal("Nimal Perera-Silva", refreshed!.FullName);
        Assert.Equal("Neurology", refreshed.Specialization);
    }

    [Fact]
    public async Task A_deactivated_doctor_drops_out_of_booking_but_keeps_their_history()
    {
        await PublishAsync(DoctorUpdated("Kamala Silva", isActive: true));
        var receptionist = _factory.CreateClientAs("Receptionist");
        var schedule = await receptionist.PostAsJsonAsync("/api/schedules", ScheduleRequest(daysAhead: 1));
        Assert.Equal(HttpStatusCode.Created, schedule.StatusCode);

        // AC2: staff.deactivated removes the doctor from bookable listings.
        await PublishAsync(new StaffDeactivatedEvent { StaffId = _doctorId });

        var doctors = await receptionist.GetFromJsonAsync<List<DoctorResponse>>(
            $"/api/doctors?specialization={_specialization}");
        Assert.Empty(doctors!);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await receptionist.GetAsync($"/api/doctors/{_doctorId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await receptionist.GetAsync($"/api/slots/available?doctorId={_doctorId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await receptionist.PostAsJsonAsync("/api/schedules", ScheduleRequest(daysAhead: 2))).StatusCode);

        var doctorClient = _factory.CreateClientAs("Doctor", _doctorId);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await doctorClient.PostAsJsonAsync("/api/doctor-leaves", LeaveRequest())).StatusCode);

        // The existing schedule is kept, not deleted: what happens to it is SCRUM-34's call.
        var history = await receptionist.GetFromJsonAsync<List<DoctorScheduleResponse>>(
            $"/api/schedules/doctor/{_doctorId}");
        Assert.Single(history!);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Delivers an event the way the consumer would: serialized as Identity writes it, read back by
    /// the real processor, applied by the real handler. Only the Kafka transport is skipped.
    /// </summary>
    private async Task PublishAsync(IntegrationEvent staffEvent)
    {
        _messageIds.Add(staffEvent.MessageId);
        var payload = JsonSerializer.Serialize(staffEvent, staffEvent.GetType(), IdentityJson);

        using var scope = _factory.Services.CreateScope();
        var processor = new StaffEventProcessor(
            scope.ServiceProvider.GetRequiredService<IStaffEventHandler>(),
            NullLogger<StaffEventProcessor>.Instance);

        var result = await processor.ProcessAsync(staffEvent.EventType, payload, CancellationToken.None);
        Assert.IsType<StaffEventAppliedResult>(result);
    }

    private StaffUpdatedEvent DoctorUpdated(string fullName, bool isActive, string? specialization = null) => new()
    {
        StaffId = _doctorId,
        FullName = fullName,
        Specialization = specialization ?? _specialization,
        DepartmentId = Guid.NewGuid(),
        Role = "Doctor",
        IsActive = isActive,
        OccurredAtUtc = DateTime.UtcNow
    };

    private CreateDoctorScheduleRequest ScheduleRequest(int daysAhead)
    {
        var today = ColomboTime.Today(TimeProvider.System);
        return new CreateDoctorScheduleRequest(
            _doctorId,
            today.AddDays(daysAhead).DayOfWeek,
            new TimeOnly(9, 0),
            new TimeOnly(12, 0),
            15,
            today,
            null);
    }

    private CreateDoctorLeaveRequest LeaveRequest()
    {
        var today = ColomboTime.Today(TimeProvider.System);
        return new CreateDoctorLeaveRequest(_doctorId, today.AddDays(10), today.AddDays(11), "Conference");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Removes every row this test wrote, children first.</summary>
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
        var messageIds = _messageIds.ToArray();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM medicore_appointment.slots WHERE \"DoctorId\" = {_doctorId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM medicore_appointment.doctor_schedules WHERE \"DoctorId\" = {_doctorId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM medicore_appointment.doctor_leaves WHERE \"DoctorId\" = {_doctorId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM medicore_appointment.cached_doctors WHERE \"DoctorId\" = {_doctorId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM medicore_appointment.processed_messages WHERE \"MessageId\" = ANY({messageIds})");
    }
}
