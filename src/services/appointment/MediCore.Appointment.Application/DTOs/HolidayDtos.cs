using MediCore.Appointment.Application.Services;

namespace MediCore.Appointment.Application.DTOs;

// ── Requests ──────────────────────────────────────────────────────────────────

/// <summary>Declares a clinic-wide non-working day.</summary>
public sealed record CreatePublicHolidayRequest(DateOnly Date, string Name);

// ── Responses ─────────────────────────────────────────────────────────────────

/// <summary>A clinic-wide closure.</summary>
public sealed record PublicHolidayResponse(
    Guid HolidayId,
    DateOnly Date,
    string Name,
    DateTime CreatedAt,
    string CreatedBy);

/// <summary>
/// A holiday together with what declaring or withdrawing it did to every doctor's slots.
/// </summary>
public sealed record PublicHolidayMutationResponse(
    PublicHolidayResponse Holiday,
    SlotReconciliationSummary Impact);
