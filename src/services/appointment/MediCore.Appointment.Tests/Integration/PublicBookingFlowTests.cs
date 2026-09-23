using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using MediCore.Appointment.Infrastructure.Messaging;
using MediCore.Appointment.Infrastructure.Persistence;
using MediCore.Contracts.Events;
using MediCore.Contracts.Events.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCore.Appointment.Tests.Integration;

/// <summary>
/// SCRUM-34: the public booking path end to end inside the API — browse anonymously, then book
/// with a booking token — with neither the Identity service nor Kafka running.
/// </summary>
/// <remarks>
/// Needs the local Postgres (<c>docker compose up -d postgres</c>). Each test uses a fresh doctor
/// and patient id and deletes everything it wrote, so it is safe against a shared development
/// database.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class PublicBookingFlowTests : IClassFixture<AppointmentApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions IdentityJson = new();

    private readonly AppointmentApiFactory _factory;
    private readonly Guid _doctorId = Guid.NewGuid();
    private readonly Guid _patientId = Guid.NewGuid();
    private readonly string _specialization;
    private readonly List<Guid> _messageIds = [];

    public PublicBookingFlowTests(AppointmentApiFactory factory)
    {
        _factory = factory;
        _specialization = $"Integration-{_doctorId:N}"[..32];
    }

    [Fact]
    public async Task A_patient_browses_anonymously_then_books_with_a_booking_token()
    {
        await GiveTheDoctorWorkingHoursAsync();

        // ── Browse, with no token at all ──────────────────────────────────────
        var anonymous = _factory.CreateClient();

        var specializations = await anonymous.GetFromJsonAsync<List<string>>(
            "/api/public/booking/specializations");
        Assert.Contains(_specialization, specializations!);

        var doctors = await anonymous.GetFromJsonAsync<List<PublicDoctorResponse>>(
            $"/api/public/booking/doctors?specialization={_specialization}");
        var doctor = Assert.Single(doctors!);
        Assert.Equal(_doctorId, doctor.DoctorId);
        Assert.Equal("Nimal Perera", doctor.FullName);

        var slots = await anonymous.GetFromJsonAsync<List<PublicSlotResponse>>(
            $"/api/public/booking/slots?doctorId={_doctorId}");
        Assert.NotEmpty(slots!);
        var chosen = slots![0];

        // ── Book, with a token naming this patient and carrying no role ───────
        var patient = _factory.CreateBookingClientFor(_patientId, "PAT-000777", "Kamala Silva");
        var response = await patient.PostAsJsonAsync(
            "/api/appointments",
            new BookAppointmentRequest(chosen.SlotId, Guid.Empty, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var appointment = (await response.Content.ReadFromJsonAsync<AppointmentResponse>())!;
        // The patient came from the claim, not from the body, which deliberately sent nothing.
        Assert.Equal(_patientId, appointment.PatientId);
        // Who booked, copied off the same signed token, so staff can see it without this service
        // asking the Patient service.
        Assert.Equal("PAT-000777", appointment.PatientNumber);
        Assert.Equal("Kamala Silva", appointment.PatientName);
        Assert.Equal(_doctorId, appointment.DoctorId);
        Assert.Equal(chosen.SlotId, appointment.SlotId);
        Assert.Equal(AppointmentStatus.Booked, appointment.Status);
        Assert.Equal(ServiceCodes.GeneralConsultation, appointment.ServiceCode);

        // ── AC1: the slot is taken and no longer offered ──────────────────────
        var remaining = await anonymous.GetFromJsonAsync<List<PublicSlotResponse>>(
            $"/api/public/booking/slots?doctorId={_doctorId}");
        Assert.DoesNotContain(remaining!, slot => slot.SlotId == chosen.SlotId);

        // ── AC4 and AC5: the event is waiting in the outbox, unpublished ──────
        // There is no broker in this host, and the booking still succeeded.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
        var row = await db.OutboxMessages
            .AsNoTracking()
            .SingleAsync(message => message.EventKey == appointment.AppointmentId.ToString());

        Assert.Equal("appointment-events", row.Topic);
        Assert.Equal("appointment.booked", row.EventType);
        Assert.Null(row.ProcessedOnUtc);
        Assert.Contains(ServiceCodes.GeneralConsultation, row.Payload);
    }

    [Fact]
    public async Task Booking_without_any_token_is_refused_even_though_browsing_is_not()
    {
        await GiveTheDoctorWorkingHoursAsync();
        var anonymous = _factory.CreateClient();

        var slots = await anonymous.GetFromJsonAsync<List<PublicSlotResponse>>(
            $"/api/public/booking/slots?doctorId={_doctorId}");

        var response = await anonymous.PostAsJsonAsync(
            "/api/appointments",
            new BookAppointmentRequest(slots![0].SlotId, _patientId, null));

        // The gateway does not authenticate, so this is the service's own refusal.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_same_patient_cannot_take_two_slots_that_overlap()
    {
        // SCRUM-34 AC3, through the real database and the real index.
        await GiveTheDoctorWorkingHoursAsync();
        var anonymous = _factory.CreateClient();
        var patient = _factory.CreateBookingClientFor(_patientId);

        var slots = await anonymous.GetFromJsonAsync<List<PublicSlotResponse>>(
            $"/api/public/booking/slots?doctorId={_doctorId}");

        var first = await patient.PostAsJsonAsync(
            "/api/appointments", new BookAppointmentRequest(slots![0].SlotId, Guid.Empty, null));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // The very same slot, which is now Booked.
        var again = await patient.PostAsJsonAsync(
            "/api/appointments", new BookAppointmentRequest(slots[0].SlotId, Guid.Empty, null));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // A different, non-adjacent slot for the same patient is fine — the clash rule is about
        // overlapping time, not about booking twice.
        var later = slots.First(slot => slot.StartUtc >= slots[0].EndUtc.AddMinutes(15));
        var second = await patient.PostAsJsonAsync(
            "/api/appointments", new BookAppointmentRequest(later.SlotId, Guid.Empty, null));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task The_public_slot_listing_publishes_nothing_about_other_patients()
    {
        await GiveTheDoctorWorkingHoursAsync();

        var raw = await _factory.CreateClient()
            .GetStringAsync($"/api/public/booking/slots?doctorId={_doctorId}");

        // Asserted on the wire, not on the DTO: this is what actually reaches a browser.
        Assert.DoesNotContain("flaggedReason", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flaggedAtUtc", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scheduleId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("status", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_unknown_doctor_cannot_be_probed_for_through_the_public_listing()
    {
        var response = await _factory.CreateClient()
            .GetAsync($"/api/public/booking/slots?doctorId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task GiveTheDoctorWorkingHoursAsync()
    {
        await PublishAsync(DoctorUpdated("Nimal Perera"));

        var receptionist = _factory.CreateClientAs("Receptionist");
        var today = ColomboTime.Today(TimeProvider.System);
        var schedule = await receptionist.PostAsJsonAsync(
            "/api/schedules",
            new CreateDoctorScheduleRequest(
                _doctorId,
                today.AddDays(1).DayOfWeek,
                new TimeOnly(9, 0),
                new TimeOnly(12, 0),
                15,
                today,
                null));

        Assert.Equal(HttpStatusCode.Created, schedule.StatusCode);
    }

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

    private StaffUpdatedEvent DoctorUpdated(string fullName) => new()
    {
        StaffId = _doctorId,
        FullName = fullName,
        Specialization = _specialization,
        DepartmentId = Guid.NewGuid(),
        Role = "Doctor",
        IsActive = true,
        OccurredAtUtc = DateTime.UtcNow
    };

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Removes every row this test wrote, children first.</summary>
    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
        var messageIds = _messageIds.ToArray();

        // The outbox row is keyed by the appointment, so it goes before the appointments do.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             DELETE FROM medicore_appointment.outbox_messages
             WHERE "EventKey" IN (
                 SELECT "AppointmentId"::text FROM medicore_appointment.appointments
                 WHERE "PatientId" = {_patientId})
             """);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM medicore_appointment.appointments WHERE \"PatientId\" = {_patientId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM medicore_appointment.slots WHERE \"DoctorId\" = {_doctorId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM medicore_appointment.doctor_schedules WHERE \"DoctorId\" = {_doctorId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM medicore_appointment.cached_doctors WHERE \"DoctorId\" = {_doctorId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM medicore_appointment.processed_messages WHERE \"MessageId\" = ANY({messageIds})");
    }
}
