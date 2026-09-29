namespace MediCore.Appointment.Application.DTOs;

// ── Requests ──────────────────────────────────────────────────────────────────

/// <summary>Suppresses a single slot without touching the schedule that produced it.</summary>
public sealed record BlockSlotRequest(string? Reason);

// ── Responses ─────────────────────────────────────────────────────────────────

/// <summary>A single bookable window.</summary>
/// <remarks>
/// <c>StartUtc</c> and <c>EndUtc</c> are instants; <c>SlotDate</c> is the Asia/Colombo calendar
/// date they fall on, which is what a clinic-facing UI groups by.
/// </remarks>
public sealed record SlotResponse(
    Guid SlotId,
    Guid DoctorId,
    Guid? ScheduleId,
    DateTime StartUtc,
    DateTime EndUtc,
    DateOnly SlotDate,
    int DurationMinutes,
    string Status,
    string? FlaggedReason,
    DateTime? FlaggedAtUtc);
