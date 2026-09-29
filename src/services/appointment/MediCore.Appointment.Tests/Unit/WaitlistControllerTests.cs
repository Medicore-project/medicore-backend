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

/// <summary>SCRUM-37: who the waitlist services are told is calling, and what each outcome answers.</summary>
public sealed class WaitlistControllerTests
{
    private static readonly Guid DoctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BodyPatientId = Guid.Parse("2222222b-2222-2222-2222-222222222222");
    private static readonly Guid StaffId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid EntryId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateOnly Day = new(2026, 9, 25);
    private static readonly DateOnly Today = new(2026, 9, 23);

    /// <summary>10:30 Colombo time on 26 Sep 2026.</summary>
    private static readonly DateTime StartUtc = new(2026, 9, 26, 5, 0, 0, DateTimeKind.Utc);

    // ── Join ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_booking_token_joins_as_its_own_patient_whatever_the_body_says()
    {
        var waitlist = new StubWaitlistService();

        var result = await PatientController(waitlist).Join(
            new JoinWaitlistRequest(DoctorId, Day, BodyPatientId, ServiceCodes.FollowUp), CancellationToken.None);

        var join = Assert.Single(waitlist.Joins);
        Assert.Equal(PatientId, join.PatientId);
        Assert.Equal(DoctorId, join.DoctorId);
        Assert.Equal(Day, join.Date);
        Assert.Equal(ServiceCodes.FollowUp, join.ServiceCode);
        Assert.Equal(new BookingPatientDetails("PAT-000042", "Nimal Perera"), join.Details);

        // The patient's reduced view: no patient or slot ids.
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.IsType<PatientWaitlistEntryResponse>(created.Value);
    }

    [Fact]
    public async Task The_front_desk_joins_for_the_patient_named_in_the_body()
    {
        var waitlist = new StubWaitlistService();

        var result = await StaffController(waitlist).Join(
            new JoinWaitlistRequest(DoctorId, Day, BodyPatientId, null), CancellationToken.None);

        Assert.Equal(BodyPatientId, Assert.Single(waitlist.Joins).PatientId);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(WaitlistController.GetById), created.ActionName);
        Assert.IsType<WaitlistEntryResponse>(created.Value);
    }

    [Fact]
    public async Task The_front_desk_must_name_a_patient()
    {
        var waitlist = new StubWaitlistService();

        var result = await StaffController(waitlist).Join(
            new JoinWaitlistRequest(DoctorId, Day, null, null), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
        Assert.Empty(waitlist.Joins);
    }

    [Fact]
    public async Task An_invalid_request_never_reaches_the_service()
    {
        var waitlist = new StubWaitlistService();

        var result = await PatientController(waitlist).Join(
            new JoinWaitlistRequest(Guid.Empty, default, null, "NOT-A-CODE"), CancellationToken.None);

        var problem = Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Contains("doctorId", problem.Errors.Keys);
        Assert.Contains("date", problem.Errors.Keys);
        Assert.Contains("serviceCode", problem.Errors.Keys);
        Assert.Empty(waitlist.Joins);
    }

    public static TheoryData<WaitlistJoinResult, int> JoinOutcomes() => new()
    {
        { new WaitlistDoctorNotFoundResult(), StatusCodes.Status404NotFound },
        { new WaitlistDateInPastResult(), StatusCodes.Status400BadRequest },
        { new WaitlistBeyondHorizonResult(60), StatusCodes.Status400BadRequest },
        { new WaitlistNoClinicThatDayResult(), StatusCodes.Status400BadRequest },
        { new WaitlistDayNotFullResult(2), StatusCodes.Status409Conflict },
        { new WaitlistAlreadyWaitingResult(), StatusCodes.Status409Conflict },
        { new WaitlistAlreadyBookedResult(), StatusCodes.Status409Conflict },
        { new WaitlistTooManyEntriesResult(3), StatusCodes.Status409Conflict },
        { new WaitlistJoinContendedResult(), StatusCodes.Status409Conflict }
    };

    [Theory]
    [MemberData(nameof(JoinOutcomes))]
    public async Task Every_join_outcome_has_its_status_code(WaitlistJoinResult outcome, int statusCode)
    {
        var result = await PatientController(new StubWaitlistService { JoinResult = outcome }).Join(
            new JoinWaitlistRequest(DoctorId, Day, null, null), CancellationToken.None);

        Assert.Equal(statusCode, StatusCodeOf(result));
    }

    [Theory]
    [InlineData(1, "That day still has a free time. Please book it instead.")]
    [InlineData(3, "That day still has 3 free times. Please book one instead.")]
    public async Task A_day_with_free_times_says_to_book_instead(int free, string title)
    {
        var result = await PatientController(new StubWaitlistService { JoinResult = new WaitlistDayNotFullResult(free) })
            .Join(new JoinWaitlistRequest(DoctorId, Day, null, null), CancellationToken.None);

        Assert.Equal(title, ProblemOf(result).Title);
    }

    // ── A patient's own entries ───────────────────────────────────────────────

    [Fact]
    public async Task A_patient_reads_only_their_own_entries_in_the_reduced_view()
    {
        var waitlist = new StubWaitlistService();

        var result = await PatientController(waitlist).Mine(CancellationToken.None);

        Assert.Equal(PatientId, waitlist.MineFor);
        var entries = Assert.IsAssignableFrom<IEnumerable<PatientWaitlistEntryResponse>>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.Single(entries);
    }

    [Fact]
    public async Task A_patient_accepts_as_the_patient_their_token_names_and_gets_204()
    {
        var changes = new StubChangeService();

        var result = await PatientController(changes: changes).AcceptMine(EntryId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var call = Assert.Single(changes.Calls);
        Assert.Equal(("accept", EntryId), (call.Action, call.EntryId));
        Assert.Equal(PatientId, call.Caller.PatientId);
    }

    [Fact]
    public async Task A_patient_decline_and_leave_answer_204()
    {
        var changes = new StubChangeService();
        var controller = PatientController(changes: changes);

        Assert.IsType<NoContentResult>(await controller.DeclineMine(EntryId, CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.LeaveMine(EntryId, CancellationToken.None));

        Assert.Equal(["decline", "withdraw"], changes.Calls.Select(call => call.Action));
        Assert.All(changes.Calls, call => Assert.Equal(PatientId, call.Caller.PatientId));
        Assert.Null(changes.Calls[1].Reason);
    }

    [Fact]
    public async Task A_refused_patient_answer_keeps_its_problem()
    {
        var changes = new StubChangeService { Result = new WaitlistOfferExpiredResult(StartUtc) };

        var result = await PatientController(changes: changes).AcceptMine(EntryId, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusCodeOf(result));
        Assert.Equal(
            "This offer expired at 10:30 on 26 Sep 2026 and has passed to the next patient.",
            ProblemOf(result).Title);
    }

    // ── The front desk ────────────────────────────────────────────────────────

    [Fact]
    public async Task The_front_desk_accepts_for_the_patient_and_gets_the_appointment()
    {
        var changes = new StubChangeService();

        var result = await StaffController(changes: changes).Accept(EntryId, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal("Appointments", created.ControllerName);
        Assert.IsType<AppointmentResponse>(created.Value);
        var call = Assert.Single(changes.Calls);
        Assert.Null(call.Caller.PatientId);
        Assert.Equal(StaffId, call.Caller.StaffId);
        Assert.Equal("desk@medicore.test", call.Caller.Actor);
    }

    [Fact]
    public async Task The_front_desk_removes_an_entry_with_its_reason_or_none()
    {
        var changes = new StubChangeService { Result = Changed() };
        var controller = StaffController(changes: changes);

        Assert.IsType<OkObjectResult>(await controller.Remove(
            EntryId, new RemoveWaitlistEntryRequest("Patient phoned."), CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.Remove(EntryId, null, CancellationToken.None));

        Assert.Equal(["Patient phoned.", null], changes.Calls.Select(call => call.Reason));
    }

    [Fact]
    public async Task A_removal_reason_longer_than_the_column_is_refused()
    {
        var changes = new StubChangeService();

        var result = await StaffController(changes: changes).Remove(
            EntryId, new RemoveWaitlistEntryRequest(new string('x', 501)), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(changes.Calls);
    }

    public static TheoryData<WaitlistChangeResult, int> ChangeOutcomes() => new()
    {
        { Changed(), StatusCodes.Status200OK },
        { new WaitlistEntryNotFoundResult(), StatusCodes.Status404NotFound },
        { new WaitlistInvalidStateResult(WaitlistStatus.Waiting, WaitlistAction.Decline), StatusCodes.Status409Conflict },
        { new WaitlistOfferExpiredResult(StartUtc), StatusCodes.Status409Conflict },
        { new WaitlistOfferDoctorNotFoundResult(), StatusCodes.Status404NotFound },
        { new WaitlistOfferedSlotUnavailableResult(), StatusCodes.Status409Conflict },
        { new WaitlistPatientOverlapResult(Guid.NewGuid(), StartUtc, StartUtc.AddMinutes(30)), StatusCodes.Status409Conflict },
        { new WaitlistChangeContendedResult(), StatusCodes.Status409Conflict }
    };

    [Theory]
    [MemberData(nameof(ChangeOutcomes))]
    public async Task Every_answer_outcome_has_its_status_code(WaitlistChangeResult outcome, int statusCode)
    {
        var result = await StaffController(changes: new StubChangeService { Result = outcome })
            .Decline(EntryId, CancellationToken.None);

        Assert.Equal(statusCode, StatusCodeOf(result));
    }

    [Theory]
    [InlineData(WaitlistAction.Accept, WaitlistStatus.Expired, "There is no open offer to accept: this entry is Expired.")]
    [InlineData(WaitlistAction.Decline, WaitlistStatus.Waiting, "There is no open offer to decline: this entry is Waiting.")]
    [InlineData(WaitlistAction.Withdraw, WaitlistStatus.Accepted, "This entry is already Accepted and is no longer on the waitlist.")]
    public void A_refused_answer_says_why_in_words(string action, string status, string title)
    {
        Assert.Equal(title, WaitlistController.DescribeInvalidState(new WaitlistInvalidStateResult(status, action)));
    }

    // ── Listing ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_defaults_to_the_next_two_weeks()
    {
        var waitlist = new StubWaitlistService();

        await StaffController(waitlist).List(null, null, null, null, CancellationToken.None);

        Assert.Equal((Today, Today.AddDays(13), (string?)null), waitlist.Listed);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData(WaitlistStatus.Offered)]
    [InlineData(WaitlistStatus.Withdrawn)]
    public async Task The_list_filters_by_a_known_status(string status)
    {
        var waitlist = new StubWaitlistService();

        var result = await StaffController(waitlist).List(DoctorId, Today, Day, status, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(status, waitlist.Listed!.Value.Status);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("Cancelled")]
    public async Task An_unknown_status_is_refused(string status)
    {
        var result = await StaffController().List(null, Today, Day, status, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(result));
    }

    [Fact]
    public async Task A_backwards_or_overlong_range_is_refused()
    {
        var controller = StaffController();

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(
            await controller.List(null, Day, Today, null, CancellationToken.None)));
        Assert.Equal(StatusCodes.Status400BadRequest, StatusCodeOf(
            await controller.List(null, Today, Today.AddDays(92), null, CancellationToken.None)));
    }

    [Fact]
    public async Task An_unknown_entry_is_404()
    {
        var result = await StaffController(new StubWaitlistService { Entry = null }).GetById(EntryId, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WaitlistEntryResponse Response(string status = WaitlistStatus.Waiting) => new(
        EntryId, DoctorId, "Dr. Silva", "Cardiology", Day, PatientId, "PAT-000042", "Nimal Perera",
        ServiceCodes.GeneralConsultation, 3, 1, status, StartUtc.AddDays(-3), null, null, null, null, null, null, null);

    private static WaitlistEntryChangedResult Changed() => new(Response(WaitlistStatus.Declined));

    private static int? StatusCodeOf(IActionResult result) => result switch
    {
        ObjectResult objectResult => objectResult.StatusCode,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => null
    };

    private static ProblemDetails ProblemOf(IActionResult result) =>
        Assert.IsAssignableFrom<ProblemDetails>(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    private static WaitlistController StaffController(
        StubWaitlistService? waitlist = null,
        StubChangeService? changes = null) =>
        CreateController(
            waitlist,
            changes,
            new Claim(ClaimTypes.Role, "Receptionist"),
            new Claim(ClaimTypes.Email, "desk@medicore.test"),
            new Claim("staffId", StaffId.ToString()));

    private static WaitlistController PatientController(
        StubWaitlistService? waitlist = null,
        StubChangeService? changes = null) =>
        CreateController(
            waitlist,
            changes,
            new Claim(AppointmentAuthorizationPolicies.PatientIdClaim, PatientId.ToString()),
            new Claim(AppointmentAuthorizationPolicies.PatientNumberClaim, "PAT-000042"),
            new Claim(AppointmentAuthorizationPolicies.PatientNameClaim, "Nimal Perera"));

    private static WaitlistController CreateController(
        StubWaitlistService? waitlist,
        StubChangeService? changes,
        params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };

        return new WaitlistController(
            new JoinWaitlistRequestValidator(),
            new RemoveWaitlistEntryRequestValidator(),
            waitlist ?? new StubWaitlistService(),
            changes ?? new StubChangeService(),
            new FixedTimeProvider(new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc)))
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class StubWaitlistService : IWaitlistService
    {
        public WaitlistJoinResult? JoinResult { get; set; }
        public WaitlistEntryResponse? Entry { get; set; } = Response();
        public List<(Guid DoctorId, DateOnly Date, Guid PatientId, BookingPatientDetails? Details, string? ServiceCode)> Joins { get; } = [];
        public Guid? MineFor { get; private set; }
        public (DateOnly From, DateOnly To, string? Status)? Listed { get; private set; }

        public Task<WaitlistJoinResult> JoinAsync(
            Guid doctorId, DateOnly date, Guid patientId, BookingPatientDetails? patientDetails,
            string? serviceCode, string actor, CancellationToken cancellationToken = default)
        {
            Joins.Add((doctorId, date, patientId, patientDetails, serviceCode));
            return Task.FromResult(JoinResult ?? new WaitlistJoinedResult(Response()));
        }

        public Task<IReadOnlyList<WaitlistEntryResponse>> GetMineAsync(
            Guid patientId, CancellationToken cancellationToken = default)
        {
            MineFor = patientId;
            return Task.FromResult<IReadOnlyList<WaitlistEntryResponse>>([Response()]);
        }

        public Task<IReadOnlyList<WaitlistEntryResponse>> ListAsync(
            Guid? doctorId, DateOnly from, DateOnly to, string? status, CancellationToken cancellationToken = default)
        {
            Listed = (from, to, status);
            return Task.FromResult<IReadOnlyList<WaitlistEntryResponse>>([Response()]);
        }

        public Task<WaitlistEntryResponse?> GetAsync(Guid waitlistEntryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entry);

        public Task<WaitlistDaysResult> GetDaysAsync(
            Guid doctorId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Days are the public booking page's.");
    }

    private sealed class StubChangeService : IWaitlistChangeService
    {
        public WaitlistChangeResult? Result { get; set; }
        public List<(string Action, Guid EntryId, AppointmentCaller Caller, string? Reason)> Calls { get; } = [];

        public Task<WaitlistChangeResult> AcceptAsync(
            Guid waitlistEntryId, AppointmentCaller caller, string correlationId, CancellationToken cancellationToken = default)
        {
            Calls.Add(("accept", waitlistEntryId, caller, null));
            return Task.FromResult(Result ?? new WaitlistAcceptedResult(Appointment(), waitlistEntryId));
        }

        public Task<WaitlistChangeResult> DeclineAsync(
            Guid waitlistEntryId, AppointmentCaller caller, CancellationToken cancellationToken = default)
        {
            Calls.Add(("decline", waitlistEntryId, caller, null));
            return Task.FromResult(Result ?? Changed());
        }

        public Task<WaitlistChangeResult> WithdrawAsync(
            Guid waitlistEntryId, AppointmentCaller caller, string? reason, CancellationToken cancellationToken = default)
        {
            Calls.Add(("withdraw", waitlistEntryId, caller, reason));
            return Task.FromResult(Result ?? Changed());
        }

        private static AppointmentResponse Appointment() => new(
            Guid.NewGuid(), PatientId, "PAT-000042", "Nimal Perera", DoctorId, Guid.NewGuid(),
            StartUtc, StartUtc.AddMinutes(30), Day, 30, ServiceCodes.GeneralConsultation,
            AppointmentStatus.Booked, StartUtc.AddDays(-1));
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
