using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Services;

// ── Discriminated union results ───────────────────────────────────────────────

public abstract record HolidayCreateResult;
public sealed record HolidayCreatedResult(PublicHolidayMutationResponse Response) : HolidayCreateResult;

/// <summary>A holiday is already declared on that date. The controller maps this to 409.</summary>
public sealed record HolidayCreateDuplicateResult(DateOnly Date) : HolidayCreateResult;

public abstract record HolidayDeleteResult;
public sealed record HolidayDeletedResult(SlotReconciliationSummary Impact) : HolidayDeleteResult;
public sealed record HolidayDeleteNotFoundResult : HolidayDeleteResult;

// ── Service contract ──────────────────────────────────────────────────────────

/// <summary>
/// Clinic-wide closures. Every mutation here reconciles <em>all</em> doctors, because a holiday
/// suppresses slots for everyone.
/// </summary>
public interface IPublicHolidayService
{
    /// <summary>Every declared holiday, earliest first.</summary>
    Task<IReadOnlyList<PublicHolidayResponse>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Declares a closure and clears that day from every doctor's calendar, flagging any bookings
    /// that already existed on it.
    /// </summary>
    Task<HolidayCreateResult> CreateAsync(
        CreatePublicHolidayRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws a closure. Slots are regenerated for that date wherever a schedule covers it.
    /// </summary>
    Task<HolidayDeleteResult> DeleteAsync(
        Guid holidayId,
        string actor,
        CancellationToken cancellationToken = default);
}
