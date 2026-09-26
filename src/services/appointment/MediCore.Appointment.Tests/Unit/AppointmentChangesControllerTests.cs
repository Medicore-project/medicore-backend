using System.Security.Claims;
using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Api.Controllers;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Services;
using MediCore.Appointment.Application.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Tests.Unit;

public sealed class AppointmentChangesControllerTests
{
    private static readonly Guid AppointmentId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StaffId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>10:30 Colombo time on 26 Sep 2026.</summary>
    private static readonly DateTime StartUtc = new(2026, 9, 26, 5, 0, 0, DateTimeKind.Utc);

    // ── Cancel: who the service is told is calling ──────────────────────────

    [Fact]
    public async Task Staff_cancel_on_behalf_of_the_patient_with_no_patient_restriction()
    {
        var service = new StubLifecycleService();
        var controller = StaffController(service);

        await controller.Cancel(AppointmentId, new CancelAppointmentRequest("Travelling"), CancellationToken.None);

        var call = Assert.Single(service.Cancels);
        Assert.Equal(AppointmentId, call.AppointmentId);
        Assert.Equal("Travelling", call.Reason);
        Assert.Equal("desk@medicore.test", call.Caller.Actor);
        Assert.Null(call.Caller.PatientId);
        Assert.Equal(StaffId, call.Caller.StaffId);
    }

    [Fact]
    public async Task A_patient_cancels_as_the_patient_their_token_names()
    {
        var service = new StubLifecycleService();
        var controller = PatientController(service);

        await controller.CancelMine(AppointmentId, new CancelAppointmentRequest("Travelling"), CancellationToken.None);

        Assert.Equal(PatientId, Assert.Single(service.Cancels).Caller.PatientId);
    }

    [Fact]
    public async Task A_patient_cancellation_answers_204_rather_than_the_full_appointment()
    {
        // The public DTOs never carry the patient or slot id; the page reloads its own list.
        var controller = PatientController(new StubLifecycleService());

        var result = await controller.CancelMine(
            AppointmentId, new CancelAppointmentRequest("Travelling"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task A_token_without_a_usable_patient_claim_is_refused()
    {
        var service = new StubLifecycleService();
        var controller = CreateController(service, new Claim(AppointmentAuthorizationPolicies.PatientIdClaim, "nope"));

        var result = await controller.CancelMine(
            AppointmentId, new CancelAppointmentRequest("Travelling"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
        Assert.Empty(service.Cancels);
    }

    [Fact]
    public async Task A_cancellation_without_a_reason_is_a_validation_problem_and_never_reaches_the_service()
    {
        var service = new StubLifecycleService();
        var controller = StaffController(service);

        var result = await controller.Cancel(AppointmentId, new CancelAppointmentRequest(" "), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(result).Value);
        Assert.Contains("reason", problem.Errors.Keys);
        Assert.Empty(service.Cancels);
    }

    // ── Results to status codes ──────────────────────────────────────────────

    [Fact]
    public async Task A_staff_cancellation_returns_the_updated_appointment()
    {
        var controller = StaffController(new StubLifecycleService());

        var result = await controller.Cancel(AppointmentId, new CancelAppointmentRequest("Travelling"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(AppointmentStatus.Cancelled, Assert.IsType<AppointmentResponse>(ok.Value).Status);
    }

    [Fact]
    public async Task Not_found_is_404()
    {
        var service = new StubLifecycleService { Result = new AppointmentNotFoundResult() };

        var staff = await StaffController(service).Cancel(
            AppointmentId, new CancelAppointmentRequest("Travelling"), CancellationToken.None);
        var patient = await PatientController(service).CancelMine(
            AppointmentId, new CancelAppointmentRequest("Travelling"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, StatusCodeOf(staff));
        Assert.Equal(StatusCodes.Status404NotFound, StatusCodeOf(patient));
    }

    [Fact]
    public async Task An_invalid_transition_is_409_naming_the_current_status()
    {
        var service = new StubLifecycleService
        {
            Result = new AppointmentInvalidTransitionResult(AppointmentStatus.Completed, AppointmentHistoryAction.Cancelled)
        };

        var result = await StaffController(service).Cancel(
            AppointmentId, new CancelAppointmentRequest("Travelling"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusCodeOf(result));
        Assert.Equal(
            "This appointment is Completed and can no longer be cancelled.",
            ProblemOf(result).Title);
    }

    [Fact]
    public async Task Inside_the_window_is_400_stating_the_policy_and_the_start_in_colombo_time()
    {
        var service = new StubLifecycleService { Result = new AppointmentInsideCancellationWindowResult(24, StartUtc) };

        var result = await StaffController(service).Cancel(
            AppointmentId, new CancelAppointmentRequest("Travelling"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        Assert.Equal(
            "Appointments can only be cancelled or rescheduled up to 24 hours before they start. "
            + "This one starts at 10:30 on 26 Sep 2026.",
            ProblemOf(result).Title);
    }

    [Fact]
    public void A_one_hour_window_is_worded_in_the_singular()
    {
        var title = AppointmentChangesController.DescribeWindow(new AppointmentInsideCancellationWindowResult(1, StartUtc));

        Assert.StartsWith("Appointments can only be cancelled or rescheduled up to 1 hour before", title);
    }

    [Fact]
    public void With_no_window_the_message_says_the_appointment_has_started()
    {
        var title = AppointmentChangesController.DescribeWindow(new AppointmentInsideCancellationWindowResult(0, StartUtc));

        Assert.Equal(
            "This appointment started at 10:30 on 26 Sep 2026, so it can no longer be cancelled or rescheduled.",
            title);
    }

    [Fact]
    public async Task Contention_is_409_not_500()
    {
        var service = new StubLifecycleService { Result = new AppointmentContendedResult() };

        var result = await StaffController(service).Cancel(
            AppointmentId, new CancelAppointmentRequest("Travelling"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusCodeOf(result));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static int? StatusCodeOf(IActionResult result) => result switch
    {
        ObjectResult objectResult => objectResult.StatusCode,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => null
    };

    private static ProblemDetails ProblemOf(IActionResult result) =>
        Assert.IsAssignableFrom<ProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    private static AppointmentChangesController StaffController(IAppointmentLifecycleService service) =>
        CreateController(
            service,
            new Claim(ClaimTypes.Role, "Receptionist"),
            new Claim(ClaimTypes.Email, "desk@medicore.test"),
            new Claim("staffId", StaffId.ToString()));

    private static AppointmentChangesController PatientController(IAppointmentLifecycleService service) =>
        CreateController(
            service,
            new Claim(AppointmentAuthorizationPolicies.PatientIdClaim, PatientId.ToString()));

    private static AppointmentChangesController CreateController(
        IAppointmentLifecycleService service,
        params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };

        return new AppointmentChangesController(new CancelAppointmentRequestValidator(), service)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class StubLifecycleService : IAppointmentLifecycleService
    {
        public AppointmentChangeResult? Result { get; set; }

        public List<(Guid AppointmentId, string Reason, AppointmentCaller Caller)> Cancels { get; } = [];

        public Task<AppointmentChangeResult> CancelAsync(
            Guid appointmentId,
            string reason,
            AppointmentCaller caller,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            Cancels.Add((appointmentId, reason, caller));
            return Task.FromResult(Result ?? new AppointmentChangedResult(Response(AppointmentStatus.Cancelled)));
        }

        private static AppointmentResponse Response(string status) => new(
            AppointmentId,
            PatientId,
            "PAT-000123",
            "Kamala Silva",
            Guid.NewGuid(),
            Guid.NewGuid(),
            StartUtc,
            StartUtc.AddMinutes(30),
            new DateOnly(2026, 9, 26),
            30,
            ServiceCodes.GeneralConsultation,
            status,
            StartUtc.AddDays(-3));
    }
}
