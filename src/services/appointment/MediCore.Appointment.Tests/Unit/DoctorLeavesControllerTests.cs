using System.Security.Claims;
using FluentValidation;
using MediCore.Appointment.Api.Controllers;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Services;
using MediCore.Appointment.Application.Validators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Appointment.Tests.Unit;

public sealed class DoctorLeavesControllerTests
{
    private static readonly Guid DoctorStaffId = Guid.Parse("3f5b9c1e-1f0a-4a7c-9b3d-7c9a2e4f6b18");
    private static readonly Guid OtherDoctorId = Guid.Parse("a1c4e8d2-55b6-4f39-8e71-2d6c0b9a4371");

    // ── Submitting leave ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_doctor_submits_leave_for_themselves()
    {
        var service = new StubDoctorLeaveService();
        var controller = CreateController(service, DoctorStaffId);

        var result = await controller.Create(Request(DoctorStaffId), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(DoctorLeavesController.GetForDoctor), created.ActionName);
        Assert.Equal(1, service.CreateCalls);
    }

    [Fact]
    public async Task A_doctor_cannot_submit_leave_for_another_doctor()
    {
        var service = new StubDoctorLeaveService();
        var controller = CreateController(service, DoctorStaffId);

        var result = await controller.Create(Request(OtherDoctorId), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
    }

    [Fact]
    public async Task A_refused_submission_never_reaches_the_service()
    {
        var service = new StubDoctorLeaveService();
        var controller = CreateController(service, DoctorStaffId);

        await controller.Create(Request(OtherDoctorId), CancellationToken.None);

        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task A_caller_with_no_staff_id_cannot_submit_leave()
    {
        // An older token predating the staffId claim must not be treated as "matches anyone".
        var service = new StubDoctorLeaveService();
        var controller = CreateController(service, staffId: null);

        var result = await controller.Create(Request(DoctorStaffId), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task An_invalid_request_is_rejected_before_the_ownership_check()
    {
        var service = new StubDoctorLeaveService();
        var controller = CreateController(service, DoctorStaffId);
        var backwards = new CreateDoctorLeaveRequest(
            DoctorStaffId,
            new DateOnly(2026, 9, 24),
            new DateOnly(2026, 9, 22),
            null);

        var result = await controller.Create(backwards, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, service.CreateCalls);
    }

    // ── Withdrawing leave ─────────────────────────────────────────────────────

    [Fact]
    public async Task Withdrawal_passes_the_callers_own_staff_id_to_the_service()
    {
        var service = new StubDoctorLeaveService
        {
            WithdrawResult = new LeaveWithdrawnResult(SlotReconciliationSummary.Empty)
        };
        var controller = CreateController(service, DoctorStaffId);

        await controller.Withdraw(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(DoctorStaffId, service.WithdrawStaffId);
    }

    [Fact]
    public async Task Withdrawing_someone_elses_leave_returns_403()
    {
        var service = new StubDoctorLeaveService { WithdrawResult = new LeaveWithdrawForbiddenResult() };
        var controller = CreateController(service, OtherDoctorId);

        var result = await controller.Withdraw(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
    }

    [Fact]
    public async Task Withdrawing_an_unknown_request_returns_404()
    {
        var service = new StubDoctorLeaveService { WithdrawResult = new LeaveWithdrawNotFoundResult() };
        var controller = CreateController(service, DoctorStaffId);

        var result = await controller.Withdraw(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task A_successful_withdrawal_returns_what_it_did_to_the_calendar()
    {
        var service = new StubDoctorLeaveService
        {
            WithdrawResult = new LeaveWithdrawnResult(new SlotReconciliationSummary(1, 6, 0, 0))
        };
        var controller = CreateController(service, DoctorStaffId);

        var result = await controller.Withdraw(Guid.NewGuid(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(6, Assert.IsType<SlotReconciliationSummary>(ok.Value).SlotsCreated);
    }

    // ── Approved leave for the booking grid ───────────────────────────────────

    [Fact]
    public async Task Approved_leave_requires_a_doctor_id()
    {
        var controller = CreateController(new StubDoctorLeaveService(), DoctorStaffId);

        var result = await controller.GetApproved(
            Guid.Empty,
            new DateOnly(2026, 9, 21),
            new DateOnly(2026, 9, 27),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Approved_leave_rejects_a_backwards_date_range()
    {
        var service = new StubDoctorLeaveService();
        var controller = CreateController(service, DoctorStaffId);

        var result = await controller.GetApproved(
            DoctorStaffId,
            new DateOnly(2026, 9, 27),
            new DateOnly(2026, 9, 21),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, service.ApprovedCalls);
    }

    [Fact]
    public async Task Approved_leave_returns_the_dates_covering_the_week()
    {
        var service = new StubDoctorLeaveService
        {
            Approved = [Response(LeaveStatus.Approved)]
        };
        var controller = CreateController(service, DoctorStaffId);

        var result = await controller.GetApproved(
            DoctorStaffId,
            new DateOnly(2026, 9, 21),
            new DateOnly(2026, 9, 27),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<DoctorLeaveResponse>>(ok.Value));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CreateDoctorLeaveRequest Request(Guid doctorId) => new(
        doctorId,
        new DateOnly(2026, 9, 22),
        new DateOnly(2026, 9, 24),
        "Conference");

    private static DoctorLeaveResponse Response(string status) => new(
        Guid.NewGuid(),
        DoctorStaffId,
        new DateOnly(2026, 9, 22),
        new DateOnly(2026, 9, 24),
        "Conference",
        status,
        null,
        null,
        null,
        DateTime.UtcNow,
        "doctor@medicore.lk");

    private static int? StatusCodeOf(IActionResult result) => result switch
    {
        ObjectResult objectResult => objectResult.StatusCode,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => null
    };

    private static DoctorLeavesController CreateController(IDoctorLeaveService service, Guid? staffId)
    {
        IValidator<CreateDoctorLeaveRequest> createValidator = new CreateDoctorLeaveRequestValidator();
        IValidator<ReviewDoctorLeaveRequest> reviewValidator = new ReviewDoctorLeaveRequestValidator();

        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, "Doctor"),
            new(ClaimTypes.Email, "doctor@medicore.lk")
        };
        if (staffId is not null)
        {
            claims.Add(new Claim("staffId", staffId.Value.ToString()));
        }

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };

        return new DoctorLeavesController(createValidator, reviewValidator, service)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class StubDoctorLeaveService : IDoctorLeaveService
    {
        public IReadOnlyList<DoctorLeaveResponse> Approved { get; set; } = [];
        public LeaveWithdrawResult WithdrawResult { get; set; } = new LeaveWithdrawNotFoundResult();
        public int CreateCalls { get; private set; }
        public int ApprovedCalls { get; private set; }
        public Guid? WithdrawStaffId { get; private set; }

        public Task<IReadOnlyList<DoctorLeaveResponse>> GetForDoctorAsync(
            Guid doctorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoctorLeaveResponse>>([]);

        public Task<IReadOnlyList<DoctorLeaveResponse>> GetApprovedBetweenAsync(
            Guid doctorId,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default)
        {
            ApprovedCalls++;
            return Task.FromResult(Approved);
        }

        public Task<IReadOnlyList<DoctorLeaveResponse>> GetPendingAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DoctorLeaveResponse>>([]);

        public Task<LeaveCreateResult> CreateAsync(
            CreateDoctorLeaveRequest request,
            string actor,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult<LeaveCreateResult>(
                new LeaveCreatedResult(Response(LeaveStatus.Pending)));
        }

        public Task<LeaveReviewResult> ReviewAsync(
            Guid leaveId,
            ReviewDoctorLeaveRequest request,
            string actor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LeaveReviewResult>(new LeaveReviewNotFoundResult());

        public Task<LeaveWithdrawResult> WithdrawAsync(
            Guid leaveId,
            string actor,
            Guid? actorStaffId,
            CancellationToken cancellationToken = default)
        {
            WithdrawStaffId = actorStaffId;
            return Task.FromResult(WithdrawResult);
        }
    }
}
