namespace MediCore.Appointment.Application.DTOs;

/// <summary>
/// A waitlist entry as staff see it.
/// </summary>
/// <param name="Position">The entry's fixed position in its queue's order; gaps are normal.</param>
/// <param name="PlaceInLine">
/// Where a waiting entry stands now: one more than the waiting entries ahead of it. Null for any
/// other status — an offered entry has left the line, and a closed one is no longer in it.
/// </param>
/// <param name="OfferedStartUtc">
/// The offered slot's start, while the slot still exists. Kept after the offer closes, as a record.
/// </param>
public sealed record WaitlistEntryResponse(
    Guid WaitlistEntryId,
    Guid DoctorId,
    string? DoctorName,
    string? Specialization,
    DateOnly SlotDate,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    string ServiceCode,
    int Position,
    int? PlaceInLine,
    string Status,
    DateTime JoinedAtUtc,
    Guid? OfferedSlotId,
    DateTime? OfferedStartUtc,
    DateTime? OfferedEndUtc,
    DateTime? OfferExpiresAtUtc,
    Guid? AppointmentId,
    DateTime? ClosedAtUtc,
    string? ClosedReason);

/// <summary>
/// A waitlist entry as its patient sees it: no patient or slot ids, as
/// <see cref="PatientAppointmentResponse"/> carries none, since the caller already knows who they
/// are and acts on the entry id alone.
/// </summary>
public sealed record PatientWaitlistEntryResponse(
    Guid WaitlistEntryId,
    Guid DoctorId,
    string? DoctorName,
    string? Specialization,
    DateOnly SlotDate,
    int? PlaceInLine,
    string Status,
    DateTime JoinedAtUtc,
    DateTime? OfferedStartUtc,
    DateTime? OfferedEndUtc,
    DateTime? OfferExpiresAtUtc,
    Guid? AppointmentId,
    DateTime? ClosedAtUtc,
    string? ClosedReason);
