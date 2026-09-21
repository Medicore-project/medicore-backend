using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IDoctorLeaveService"/>
public sealed class DoctorLeaveService : IDoctorLeaveService
{
    private readonly IDoctorLeaveRepository _repository;
    private readonly IScheduleRevisionService _revisionService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public DoctorLeaveService(
        IDoctorLeaveRepository repository,
        IScheduleRevisionService revisionService,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _revisionService = revisionService;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<DoctorLeaveResponse>> GetForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default)
    {
        var leaves = await _repository.GetAllForDoctorAsync(doctorId, cancellationToken);
        return leaves.Select(ToResponse).ToList();
    }

    public async Task<IReadOnlyList<DoctorLeaveResponse>> GetApprovedBetweenAsync(
        Guid doctorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var leaves = await _repository.GetApprovedForDoctorBetweenAsync(doctorId, from, to, cancellationToken);
        return leaves.Select(ToResponse).ToList();
    }

    public async Task<IReadOnlyList<DoctorLeaveResponse>> GetPendingAsync(
        CancellationToken cancellationToken = default)
    {
        var leaves = await _repository.GetPendingAsync(cancellationToken);
        return leaves.Select(ToResponse).ToList();
    }

    public async Task<LeaveCreateResult> CreateAsync(
        CreateDoctorLeaveRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var leave = new DoctorLeave
        {
            DoctorId = request.DoctorId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Reason = request.Reason,

            // Set explicitly rather than relying on the entity initialiser, so that the one status
            // a new request may hold is stated where the request is built.
            Status = LeaveStatus.Pending,
            CreatedBy = actor,
        };

        await _repository.AddAsync(leave, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Deliberately no reconciliation: a pending request must leave the calendar untouched.
        return new LeaveCreatedResult(ToResponse(leave));
    }

    public async Task<LeaveReviewResult> ReviewAsync(
        Guid leaveId,
        ReviewDoctorLeaveRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var leave = await _repository.GetTrackedByLeaveIdAsync(leaveId, cancellationToken);

        if (leave is null)
        {
            return new LeaveReviewNotFoundResult();
        }

        var from = leave.Status;
        var to = request.Decision;

        if (!LeaveStatus.CanTransitionTo(from, to))
        {
            return new LeaveReviewInvalidTransitionResult(from, to);
        }

        leave.Status = to;
        leave.ReviewedBy = actor;
        leave.ReviewedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        leave.ReviewNotes = request.Notes;
        leave.UpdatedBy = actor;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Only a decision that crosses the approved boundary changes which slots should exist.
        // Pending to Rejected, for instance, leaves the calendar exactly as it was.
        var affectsSlots = LeaveStatus.SuppressesSlots(from) || LeaveStatus.SuppressesSlots(to);

        var impact = affectsSlots
            ? await _revisionService.ReconcileDoctorAsync(
                leave.DoctorId,
                $"Leave {to.ToLowerInvariant()} for {leave.StartDate:yyyy-MM-dd} to {leave.EndDate:yyyy-MM-dd}.",
                cancellationToken)
            : SlotReconciliationSummary.Empty;

        return new LeaveReviewedResult(new DoctorLeaveReviewResponse(ToResponse(leave), impact));
    }

    public async Task<LeaveWithdrawResult> WithdrawAsync(
        Guid leaveId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var leave = await _repository.GetTrackedByLeaveIdAsync(leaveId, cancellationToken);

        if (leave is null)
        {
            return new LeaveWithdrawNotFoundResult();
        }

        // Withdrawal is a soft delete rather than a fourth status — see DoctorLeave.
        var wasSuppressing = LeaveStatus.SuppressesSlots(leave.Status);

        leave.IsDeleted = true;
        leave.UpdatedBy = actor;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var impact = wasSuppressing
            ? await _revisionService.ReconcileDoctorAsync(
                leave.DoctorId,
                $"Approved leave for {leave.StartDate:yyyy-MM-dd} to {leave.EndDate:yyyy-MM-dd} was withdrawn.",
                cancellationToken)
            : SlotReconciliationSummary.Empty;

        return new LeaveWithdrawnResult(impact);
    }

    private static DoctorLeaveResponse ToResponse(DoctorLeave leave) => new(
        leave.LeaveId,
        leave.DoctorId,
        leave.StartDate,
        leave.EndDate,
        leave.Reason,
        leave.Status,
        leave.ReviewedBy,
        leave.ReviewedAtUtc,
        leave.ReviewNotes,
        leave.CreatedAt,
        leave.CreatedBy);
}
