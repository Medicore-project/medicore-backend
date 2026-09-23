using System.Reflection;
using MediCore.Appointment.Api.Controllers;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediCore.Appointment.Tests.Unit;

public sealed class PublicBookingControllerTests
{
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DepartmentId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SlotId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime SlotStart = new(2026, 9, 24, 3, 30, 0, DateTimeKind.Utc);

    // ── What the public page is allowed to see ───────────────────────────────

    [Fact]
    public async Task The_public_doctor_listing_drops_the_internal_department_id()
    {
        var controller = CreateController();

        var result = await controller.GetDoctors(specialization: null, CancellationToken.None);

        var doctors = Assert.IsType<List<PublicDoctorResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        var doctor = Assert.Single(doctors);
        Assert.Equal(DoctorId, doctor.DoctorId);
        Assert.Equal("Nimal Perera", doctor.FullName);
        Assert.Equal("Neurology", doctor.Specialization);
    }

    [Fact]
    public void The_public_doctor_shape_cannot_quietly_regrow_a_field()
    {
        // Everything on this record is published to the whole internet, because the gateway does
        // not authenticate. Adding a field should be a decision, not a side effect of reusing a
        // staff-facing DTO — so this fails the moment the shape changes.
        Assert.Equal(
            ["DoctorId", "FullName", "Specialization"],
            PropertyNamesOf<PublicDoctorResponse>());
    }

    [Fact]
    public async Task The_public_slot_listing_never_carries_another_patients_flag_reason()
    {
        // FlaggedReason is free text a staff member wrote about someone else's stranded booking.
        // It is the single most important omission on this page.
        var controller = CreateController();

        var result = await controller.GetSlots(DoctorId, from: null, to: null, CancellationToken.None);

        var slots = Assert.IsType<List<PublicSlotResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        var slot = Assert.Single(slots);
        Assert.Equal(SlotId, slot.SlotId);
        Assert.Equal(SlotStart, slot.StartUtc);
        Assert.Equal(30, slot.DurationMinutes);
    }

    [Fact]
    public void The_public_slot_shape_cannot_quietly_regrow_a_field()
    {
        Assert.Equal(
            ["SlotId", "StartUtc", "EndUtc", "SlotDate", "DurationMinutes"],
            PropertyNamesOf<PublicSlotResponse>());
    }

    // ── Specializations ──────────────────────────────────────────────────────

    [Fact]
    public async Task Specializations_are_listed_for_anyone_to_browse()
    {
        var controller = CreateController();

        var result = await controller.GetSpecializations(CancellationToken.None);

        Assert.Equal(
            ["Cardiology", "Neurology"],
            Assert.IsType<OkObjectResult>(result).Value as IReadOnlyList<string>);
    }

    // ── Guards inherited from the staff listing ──────────────────────────────

    [Fact]
    public async Task A_doctor_who_is_not_bookable_is_a_404_rather_than_an_empty_list()
    {
        // Inherited from ISlotService, so an unknown or deactivated doctor cannot be probed for
        // through the public endpoint any more than through the staff one.
        var controller = CreateController(slots: new FakeSlotService(doctorIsBookable: false));

        var result = await controller.GetSlots(DoctorId, null, null, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(
            "Doctor not found or not bookable.",
            Assert.IsType<ProblemDetails>(notFound.Value).Title);
    }

    [Fact]
    public async Task Asking_for_slots_without_a_doctor_is_a_bad_request()
    {
        var slots = new FakeSlotService();
        var controller = CreateController(slots: slots);

        var result = await controller.GetSlots(Guid.Empty, null, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, slots.QueryCount);
    }

    [Fact]
    public async Task A_backwards_date_range_is_a_bad_request()
    {
        var slots = new FakeSlotService();
        var controller = CreateController(slots: slots);

        var result = await controller.GetSlots(
            DoctorId,
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 9, 24),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, slots.QueryCount);
    }

    // ── The shape of the anonymous surface ───────────────────────────────────

    [Fact]
    public void Only_this_controller_is_anonymous()
    {
        Assert.NotNull(typeof(PublicBookingController).GetCustomAttribute<AllowAnonymousAttribute>());

        // The staff-facing controllers must stay shut. Their own guard tests cover them; this one
        // states the intent of the split from the public side.
        Assert.NotNull(typeof(DoctorsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(typeof(SlotsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(typeof(AppointmentsController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void The_anonymous_reads_are_rate_limited_more_tightly_than_the_rest()
    {
        var attribute = typeof(PublicBookingController).GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(PublicRateLimitPolicies.PublicBooking, attribute.PolicyName);
    }

    [Fact]
    public void The_public_controller_exposes_reads_only()
    {
        // No booking here: POST /api/appointments needs a token naming the patient.
        var writeVerbs = typeof(PublicBookingController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method =>
                method.GetCustomAttribute<HttpPostAttribute>() is not null
                || method.GetCustomAttribute<HttpPutAttribute>() is not null
                || method.GetCustomAttribute<HttpPatchAttribute>() is not null
                || method.GetCustomAttribute<HttpDeleteAttribute>() is not null);

        Assert.Empty(writeVerbs);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string[] PropertyNamesOf<T>() =>
        [.. typeof(T).GetProperties().Select(property => property.Name)];

    private static PublicBookingController CreateController(
        FakeDoctorDirectoryService? doctors = null,
        FakeSlotService? slots = null) =>
        new(doctors ?? new FakeDoctorDirectoryService(), slots ?? new FakeSlotService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private sealed class FakeDoctorDirectoryService : IDoctorDirectoryService
    {
        public Task<IReadOnlyList<DoctorResponse>> ListBookableAsync(
            string? specialization, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoctorResponse>>(
                [new DoctorResponse(DoctorId, "Nimal Perera", "Neurology", DepartmentId)]);

        public Task<IReadOnlyList<string>> ListSpecializationsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["Cardiology", "Neurology"]);

        public Task<DoctorResponse?> GetBookableAsync(
            Guid doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The public page lists doctors; it never fetches one.");
    }

    private sealed class FakeSlotService : ISlotService
    {
        private readonly bool _doctorIsBookable;

        public FakeSlotService(bool doctorIsBookable = true)
        {
            _doctorIsBookable = doctorIsBookable;
        }

        public int QueryCount { get; private set; }

        public Task<AvailableSlotsResult> GetAvailableAsync(
            Guid doctorId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
        {
            QueryCount++;

            if (!_doctorIsBookable)
            {
                return Task.FromResult<AvailableSlotsResult>(new AvailableSlotsDoctorNotFoundResult());
            }

            return Task.FromResult<AvailableSlotsResult>(new AvailableSlotsFoundResult(
            [
                new SlotResponse(
                    SlotId,
                    doctorId,
                    Guid.NewGuid(),
                    SlotStart,
                    SlotStart.AddMinutes(30),
                    new DateOnly(2026, 9, 24),
                    30,
                    "Available",
                    // Present on the staff shape, and must not survive the mapping.
                    "Rescheduled after Dr Perera's leave",
                    SlotStart.AddDays(-1))
            ]));
        }

        public Task<IReadOnlyList<SlotResponse>> GetFlaggedAsync(
            Guid? doctorId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The attention list is never public.");

        public Task<SlotBlockResult> BlockAsync(
            Guid slotId, BlockSlotRequest request, string actor,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The public page never writes.");

        public Task<SlotUnblockResult> UnblockAsync(
            Guid slotId, string actor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The public page never writes.");
    }
}
