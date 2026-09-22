using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Services;

namespace MediCore.Appointment.Application.DTOs;

// ── Requests ──────────────────────────────────────────────────────────────────

/// <summary>
/// Submits a leave request. The result is always <see cref="LeaveStatus.Pending"/> — creating a
/// request grants nothing and changes no slots until an administrator approves it.
/// </summary>
public sealed record CreateDoctorLeaveRequest(
    Guid DoctorId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason);

/// <summary>
/// An administrator's decision on a pending request.
/// </summary>
/// <param name="Decision">
/// Either <see cref="LeaveStatus.Approved"/> or <see cref="LeaveStatus.Rejected"/>.
/// </param>
/// <param name="Notes">Why — especially worth filling in for a rejection.</param>
public sealed record ReviewDoctorLeaveRequest(string Decision, string? Notes);

// ── Responses ─────────────────────────────────────────────────────────────────

/// <summary>A leave request and where it stands.</summary>
public sealed record DoctorLeaveResponse(
    Guid LeaveId,
    Guid DoctorId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Reason,
    string Status,
    string? ReviewedBy,
    DateTime? ReviewedAtUtc,
    string? ReviewNotes,
    DateTime CreatedAt,
    string CreatedBy);

/// <summary>
/// A decided request together with what the decision did to the doctor's slots.
/// </summary>
/// <remarks>
/// Approving leave deletes the doctor's free slots for those dates and flags any booked ones, so
/// <see cref="SlotReconciliationSummary.SlotsFlagged"/> tells the approver immediately how many
/// patients are affected by the decision they just made.
/// </remarks>
public sealed record DoctorLeaveReviewResponse(
    DoctorLeaveResponse Leave,
    SlotReconciliationSummary Impact);
