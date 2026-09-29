using MediCore.Appointment.Application.Services;

namespace MediCore.Appointment.Application.DTOs;

// ── Requests ──────────────────────────────────────────────────────────────────

/// <summary>Creates one weekday's working window for a doctor.</summary>
/// <remarks>
/// A full weekly schedule is several of these — one per working day. Times are Asia/Colombo
/// wall clock.
/// </remarks>
public sealed record CreateDoctorScheduleRequest(
    Guid DoctorId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int SlotDurationMinutes,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>
/// Updates an existing schedule. The doctor and weekday are fixed at creation — moving a
/// schedule to another day means deleting it and creating a new one, so that the slots it
/// produced are reconciled correctly.
/// </summary>
public sealed record UpdateDoctorScheduleRequest(
    TimeOnly StartTime,
    TimeOnly EndTime,
    int SlotDurationMinutes,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive);

// ── Responses ─────────────────────────────────────────────────────────────────

/// <summary>A doctor's working window for one weekday.</summary>
public sealed record DoctorScheduleResponse(
    Guid ScheduleId,
    Guid DoctorId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int SlotDurationMinutes,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    DateTime CreatedAt,
    string CreatedBy);

/// <summary>
/// A schedule together with what changing it did to the doctor's slots.
/// </summary>
/// <remarks>
/// The impact is returned rather than hidden because <see cref="SlotReconciliationSummary.SlotsFlagged"/>
/// counts patients whose appointment no longer fits and who somebody now has to contact.
/// </remarks>
public sealed record DoctorScheduleMutationResponse(
    DoctorScheduleResponse Schedule,
    SlotReconciliationSummary Impact);
