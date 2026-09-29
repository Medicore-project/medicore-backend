using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Services;

// ── Discriminated union results ───────────────────────────────────────────────

public abstract record AvailableSlotsResult;
public sealed record AvailableSlotsFoundResult(IReadOnlyList<SlotResponse> Slots) : AvailableSlotsResult;

/// <summary>
/// The doctor is not in the doctor cache, or is there but no longer bookable (SCRUM-33 AC2). The
/// controller maps this to 404. Their existing slot rows are left alone.
/// </summary>
public sealed record AvailableSlotsDoctorNotFoundResult : AvailableSlotsResult;

public abstract record SlotBlockResult;
public sealed record SlotBlockedResult(SlotResponse Slot) : SlotBlockResult;
public sealed record SlotBlockNotFoundResult : SlotBlockResult;

/// <summary>
/// The slot exists but is not free to block — it is booked, already blocked, or flagged.
/// The controller maps this to 409.
/// </summary>
public sealed record SlotBlockNotAvailableResult(string CurrentStatus) : SlotBlockResult;

public abstract record SlotUnblockResult;
public sealed record SlotUnblockedResult(SlotResponse Slot) : SlotUnblockResult;
public sealed record SlotUnblockNotFoundResult : SlotUnblockResult;
public sealed record SlotUnblockNotBlockedResult(string CurrentStatus) : SlotUnblockResult;

// ── Service contract ──────────────────────────────────────────────────────────

/// <summary>Read and administrative operations over generated slots.</summary>
public interface ISlotService
{
    /// <summary>
    /// Bookable slots for a doctor over a date range, defaulting to the configured horizon.
    /// </summary>
    /// <remarks>
    /// This is the read the public guest-booking flow (SCRUM-39) is intended to reuse, so it
    /// returns nothing a caller could not legitimately book: free slots only, future only, and none
    /// at all for a doctor who is not bookable.
    /// </remarks>
    Task<AvailableSlotsResult> GetAvailableAsync(
        Guid doctorId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Slots needing attention — bookings stranded by a schedule, holiday or leave change.
    /// Pass a doctor to narrow, or null for the whole clinic.
    /// </summary>
    Task<IReadOnlyList<SlotResponse>> GetFlaggedAsync(
        Guid? doctorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suppresses a single free slot, leaving the schedule that produced it untouched.
    /// </summary>
    /// <remarks>
    /// Only a free slot can be blocked. Blocking a booked one would hide an appointment that still
    /// exists, so that is refused — cancelling the booking comes first (SCRUM-34).
    /// </remarks>
    Task<SlotBlockResult> BlockAsync(
        Guid slotId,
        BlockSlotRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a blocked slot to bookable.</summary>
    Task<SlotUnblockResult> UnblockAsync(
        Guid slotId,
        string actor,
        CancellationToken cancellationToken = default);
}
