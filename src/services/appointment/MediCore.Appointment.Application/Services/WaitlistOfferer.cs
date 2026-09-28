using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using Microsoft.Extensions.Options;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IWaitlistOfferer"/>
public sealed class WaitlistOfferer : IWaitlistOfferer
{
    /// <summary>
    /// Why an entry was taken out of the queue at offer time. The patient has a time that day
    /// already — booked while waiting, or the very appointment whose cancellation freed this slot —
    /// so offering them another would only hold it from someone who needs it.
    /// </summary>
    public const string AlreadyBookedReason = "Already booked with this doctor that day.";

    private readonly IWaitlistRepository _waitlistRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IDoctorCacheRepository _doctorRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly WaitlistOptions _options;

    public WaitlistOfferer(
        IWaitlistRepository waitlistRepository,
        IAppointmentRepository appointmentRepository,
        IDoctorCacheRepository doctorRepository,
        IUnitOfWork unitOfWork,
        IOptions<WaitlistOptions> options)
    {
        _waitlistRepository = waitlistRepository;
        _appointmentRepository = appointmentRepository;
        _doctorRepository = doctorRepository;
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    public async Task<WaitlistEntry?> OfferAsync(
        Slot slot,
        DateTime nowUtc,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (slot.Status != SlotStatus.Available)
        {
            return null;
        }

        // Too close to its start for anyone to answer and still arrive: it goes back to the
        // public list, where someone already in the building can take it.
        if (slot.StartUtc < nowUtc + _options.EffectiveMinimumLead)
        {
            return null;
        }

        // A doctor who has left takes nobody new; their queues are closed by the sweeper.
        if (await _doctorRepository.GetActiveAsync(slot.DoctorId, cancellationToken) is null)
        {
            return null;
        }

        await _waitlistRepository.LockQueueAsync(slot.DoctorId, slot.SlotDate, cancellationToken);

        var waiting = await _waitlistRepository.GetWaitingTrackedAsync(
            slot.DoctorId, slot.SlotDate, cancellationToken);

        foreach (var entry in waiting)
        {
            // The query ran in the database, but EF hands back the tracked copy of any entry this
            // unit of work already holds. One offered a moment ago in the same transaction still
            // reads Waiting there, and must not be offered a second slot.
            if (entry.Status != WaitlistStatus.Waiting)
            {
                continue;
            }

            if (await _waitlistRepository.HasBookedWithDoctorOnAsync(
                    entry.PatientId, slot.DoctorId, slot.SlotDate, cancellationToken))
            {
                Close(entry, WaitlistStatus.Withdrawn, AlreadyBookedReason, nowUtc, actor);
                continue;
            }

            // Busy at exactly this time elsewhere: they could not accept, so the slot goes to the
            // next in line. They keep their place for the next release, which may suit them.
            if (await _appointmentRepository.FindPatientOverlapAsync(
                    entry.PatientId, slot.StartUtc, slot.EndUtc, cancellationToken: cancellationToken) is not null)
            {
                continue;
            }

            slot.Status = SlotStatus.Offered;
            slot.UpdatedBy = actor;

            entry.Status = WaitlistStatus.Offered;
            entry.OfferedSlotId = slot.SlotId;
            entry.OfferedAtUtc = nowUtc;
            entry.OfferExpiresAtUtc = OfferExpiry(slot, nowUtc);
            entry.UpdatedBy = actor;

            return entry;
        }

        return null;
    }

    public async Task<WaitlistEntry?> PassOnAsync(
        WaitlistEntry entry,
        Slot? slot,
        string closingStatus,
        string? reason,
        DateTime nowUtc,
        string actor,
        CancellationToken cancellationToken = default)
    {
        Close(entry, closingStatus, reason, nowUtc, actor);

        // Only the slot this entry was holding is freed. Anything else — the slot already booked,
        // blocked, or held for a different entry — is left exactly as it is.
        var holding = slot is not null
            && slot.Status == SlotStatus.Offered
            && slot.SlotId == entry.OfferedSlotId;

        if (holding)
        {
            slot!.Status = SlotStatus.Available;
            slot.UpdatedBy = actor;
        }

        // See the interface: the old offer must leave the database before the next is written.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return holding
            ? await OfferAsync(slot!, nowUtc, actor, cancellationToken)
            : null;
    }

    /// <summary>
    /// The offer window from now, but never past the slot's start — an offer for a time that has
    /// begun is worthless.
    /// </summary>
    private DateTime OfferExpiry(Slot slot, DateTime nowUtc)
    {
        var windowEnd = nowUtc + _options.EffectiveOfferWindow;
        return windowEnd < slot.StartUtc ? windowEnd : slot.StartUtc;
    }

    private static void Close(
        WaitlistEntry entry,
        string status,
        string? reason,
        DateTime nowUtc,
        string actor)
    {
        entry.Status = status;
        entry.ClosedAtUtc = nowUtc;
        entry.ClosedReason = reason;
        entry.UpdatedBy = actor;
    }
}
