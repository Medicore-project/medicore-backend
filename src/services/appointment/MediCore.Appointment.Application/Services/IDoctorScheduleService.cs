using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Services;

// ── Discriminated union results ───────────────────────────────────────────────
// Same pattern as the Patient service's IPrescriptionService.

public abstract record ScheduleCreateResult;
public sealed record ScheduleCreatedResult(DoctorScheduleMutationResponse Response) : ScheduleCreateResult;

/// <summary>
/// The schedule would collide with one the doctor already has on that weekday (SCRUM-32 AC4).
/// The controller maps this to 409.
/// </summary>
public sealed record ScheduleCreateOverlapResult(Guid ConflictingScheduleId, DayOfWeek DayOfWeek)
    : ScheduleCreateResult;

/// <summary>
/// The doctor is not in the doctor cache, or is there but no longer bookable (SCRUM-33). The
/// controller maps this to 404.
/// </summary>
public sealed record ScheduleCreateDoctorNotFoundResult : ScheduleCreateResult;

public abstract record ScheduleUpdateResult;
public sealed record ScheduleUpdatedResult(DoctorScheduleMutationResponse Response) : ScheduleUpdateResult;
public sealed record ScheduleUpdateNotFoundResult : ScheduleUpdateResult;
public sealed record ScheduleUpdateOverlapResult(Guid ConflictingScheduleId, DayOfWeek DayOfWeek)
    : ScheduleUpdateResult;

public abstract record ScheduleDeleteResult;
public sealed record ScheduleDeletedResult(SlotReconciliationSummary Impact) : ScheduleDeleteResult;
public sealed record ScheduleDeleteNotFoundResult : ScheduleDeleteResult;

public abstract record ScheduleRegenerateResult;
public sealed record ScheduleRegeneratedResult(SlotReconciliationSummary Impact) : ScheduleRegenerateResult;
public sealed record ScheduleRegenerateNotFoundResult : ScheduleRegenerateResult;

// ── Service contract ──────────────────────────────────────────────────────────

/// <summary>
/// Application-layer service for doctor schedules. Owns overlap rejection and makes sure every
/// change to a schedule is followed by reconciling that doctor's slots.
/// </summary>
public interface IDoctorScheduleService
{
    /// <summary>Every schedule for a doctor, paused ones included.</summary>
    Task<IReadOnlyList<DoctorScheduleResponse>> GetForDoctorAsync(
        Guid doctorId,
        CancellationToken cancellationToken = default);

    /// <summary>A single schedule by business key, or null when it does not exist.</summary>
    Task<DoctorScheduleResponse?> GetByIdAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a schedule and generates the slots it implies.</summary>
    Task<ScheduleCreateResult> CreateAsync(
        CreateDoctorScheduleRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a schedule's hours, slot length, effective dates or active flag, then reconciles.
    /// The doctor and weekday cannot change — see <see cref="UpdateDoctorScheduleRequest"/>.
    /// </summary>
    Task<ScheduleUpdateResult> UpdateAsync(
        Guid scheduleId,
        UpdateDoctorScheduleRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a schedule and reconciles, which clears its future free slots and flags any
    /// bookings that were relying on it.
    /// </summary>
    Task<ScheduleDeleteResult> DeleteAsync(
        Guid scheduleId,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs reconciliation for the schedule's doctor without changing the schedule.
    /// </summary>
    /// <remarks>
    /// Useful after the horizon rolls forward, and as the recovery path if a create or update
    /// committed but its follow-up reconciliation did not.
    /// </remarks>
    Task<ScheduleRegenerateResult> RegenerateAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default);
}
