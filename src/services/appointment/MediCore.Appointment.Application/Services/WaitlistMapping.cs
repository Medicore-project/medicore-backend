using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;

namespace MediCore.Appointment.Application.Services;

/// <summary>
/// Places in line, and waitlist entries as the API returns them.
/// </summary>
public static class WaitlistMapping
{
    /// <summary>
    /// Where a waiting entry stands in its queue: one more than the number of waiting entries in
    /// the same queue with a lower position. Positions are fixed at join and only ever grow, so the
    /// order is first come, first served, and an entry leaving moves everyone behind it up without
    /// any row changing. Null for an entry that is not waiting.
    /// </summary>
    /// <param name="waiting">
    /// The waiting positions of at least this entry's queue; other queues' are ignored.
    /// </param>
    public static int? PlaceInLine(WaitlistEntry entry, IEnumerable<WaitingPosition> waiting)
    {
        if (entry.Status != WaitlistStatus.Waiting)
        {
            return null;
        }

        return 1 + waiting.Count(other =>
            other.DoctorId == entry.DoctorId
            && other.SlotDate == entry.SlotDate
            && other.Position < entry.Position);
    }

    public static WaitlistEntryResponse ToResponse(WaitlistListing listing, int? placeInLine)
    {
        var entry = listing.Entry;

        return new WaitlistEntryResponse(
            entry.WaitlistEntryId,
            entry.DoctorId,
            listing.DoctorName,
            listing.DoctorSpecialization,
            entry.SlotDate,
            entry.PatientId,
            entry.PatientNumber,
            entry.PatientName,
            entry.ServiceCode,
            entry.Position,
            placeInLine,
            entry.Status,
            entry.JoinedAtUtc,
            entry.OfferedSlotId,
            listing.OfferedStartUtc,
            listing.OfferedEndUtc,
            entry.OfferExpiresAtUtc,
            entry.AppointmentId,
            entry.ClosedAtUtc,
            entry.ClosedReason);
    }

    public static PatientWaitlistEntryResponse ToPatientResponse(WaitlistEntryResponse entry) => new(
        entry.WaitlistEntryId,
        entry.DoctorId,
        entry.DoctorName,
        entry.Specialization,
        entry.SlotDate,
        entry.PlaceInLine,
        entry.Status,
        entry.JoinedAtUtc,
        entry.OfferedStartUtc,
        entry.OfferedEndUtc,
        entry.OfferExpiresAtUtc,
        entry.AppointmentId,
        entry.ClosedAtUtc,
        entry.ClosedReason);
}
