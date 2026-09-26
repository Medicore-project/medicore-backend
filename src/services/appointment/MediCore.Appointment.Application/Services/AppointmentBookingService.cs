using MediCore.Appointment.Application.Concurrency;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Exceptions;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Messaging;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IAppointmentBookingService"/>
/// <remarks>
/// There is deliberately no publisher among these dependencies. Booking cannot reach Kafka even if
/// it wanted to: it writes an outbox row and the background dispatcher does the rest, which is what
/// makes SCRUM-34 AC5 — "when Kafka is unavailable the appointment still saves" — a structural
/// property rather than a hope.
/// </remarks>
public sealed class AppointmentBookingService : IAppointmentBookingService
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ISlotRepository _slotRepository;
    private readonly IDoctorCacheRepository _doctorRepository;
    private readonly IOutboxMessageRepository _outboxRepository;
    private readonly IAppointmentHistoryRepository _historyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public AppointmentBookingService(
        IAppointmentRepository appointmentRepository,
        ISlotRepository slotRepository,
        IDoctorCacheRepository doctorRepository,
        IOutboxMessageRepository outboxRepository,
        IAppointmentHistoryRepository historyRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _appointmentRepository = appointmentRepository;
        _slotRepository = slotRepository;
        _doctorRepository = doctorRepository;
        _outboxRepository = outboxRepository;
        _historyRepository = historyRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    /// <remarks>
    /// <para>
    /// SCRUM-35. Three layers keep two patients off one slot, and one patient off two overlapping
    /// slots, however many requests arrive at once:
    /// </para>
    /// <list type="number">
    /// <item>A per-patient lock, taken first inside the transaction, so one patient's bookings run
    /// one at a time and the overlap check always sees the previous booking.</item>
    /// <item>The slot row's optimistic concurrency token: of two bookings that both read the slot
    /// Available, the later save matches no row and loses.</item>
    /// <item><c>ux_appointments_slot</c>, one active appointment per slot, as the backstop.</item>
    /// </list>
    /// <para>
    /// A lost token race (or a deadlock) re-runs the whole attempt in a new transaction, which
    /// re-reads the slot and answers from what it now is. Losing to the index is not retried: the
    /// slot is taken, and that is already the answer. Every attempt commits the slot change, the
    /// appointment and the outbox row together or not at all, so a loser leaves nothing behind.
    /// </para>
    /// </remarks>
    public async Task<BookingResult> BookAsync(
        Guid slotId,
        Guid patientId,
        string? serviceCode,
        string actor,
        string correlationId,
        BookingPatientDetails? patientDetails = null,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = await _unitOfWork.ExecuteInTransactionAsync(
                    token => AttemptAsync(
                        slotId, patientId, serviceCode, actor, correlationId, patientDetails, token),
                    cancellationToken);

                // A retry that now finds the slot Booked is the loser of the race it was retrying,
                // so it says so in the race's words rather than as a plain status.
                return attempt > 1 && result is BookingSlotNotAvailableResult { CurrentStatus: SlotStatus.Booked }
                    ? new BookingSlotTakenResult()
                    : result;
            }
            catch (SlotAlreadyBookedException)
            {
                // ux_appointments_slot caught a racer that got past the slot token — the backstop.
                // The transaction rolled back, taking the slot change and the outbox row with it.
                return new BookingSlotTakenResult();
            }
            catch (ConcurrentUpdateException) when (attempt < ConcurrencyRetry.MaxAttempts)
            {
                // The slot changed under us, or Postgres broke a deadlock. Nothing committed and
                // nothing is tracked, so go round and read everything again.
            }
            catch (ConcurrentUpdateException)
            {
                return new BookingContendedResult();
            }
        }
    }

    /// <summary>
    /// One read-decide-write, run inside a transaction by <see cref="BookAsync"/>. Throws rather
    /// than returning when the save loses a race, so the transaction rolls back.
    /// </summary>
    private async Task<BookingResult> AttemptAsync(
        Guid slotId,
        Guid patientId,
        string? serviceCode,
        string actor,
        string correlationId,
        BookingPatientDetails? patientDetails,
        CancellationToken cancellationToken)
    {
        // First, before any read: held until this transaction ends, so a concurrent booking for
        // the same patient waits here and then sees this one in its overlap check.
        await _appointmentRepository.LockPatientAsync(patientId, cancellationToken);

        // Tracked, because booking mutates the slot. This lookup filters on nothing but the key,
        // so every guard below is this method's responsibility.
        var slot = await _slotRepository.GetTrackedBySlotIdAsync(slotId, cancellationToken);

        if (slot is null)
        {
            return new BookingSlotNotFoundResult();
        }

        // Second rather than first, unlike every other service here: elsewhere the caller supplies
        // the doctor id, but here it comes off the slot, so it cannot be checked any earlier.
        if (await _doctorRepository.GetActiveAsync(slot.DoctorId, cancellationToken) is null)
        {
            return new BookingDoctorNotFoundResult();
        }

        // Before the time check: "this slot is blocked" is a more fundamental objection than
        // "this slot has passed", and it is the more useful thing to tell the caller.
        if (!string.Equals(slot.Status, SlotStatus.Available, StringComparison.Ordinal))
        {
            return new BookingSlotNotAvailableResult(slot.Status);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // SCRUM-34 AC2. The exact complement of the availability query's StartUtc >= nowUtc, so
        // nothing the listing offers is refused here and nothing it hides is accepted.
        if (slot.StartUtc <= nowUtc)
        {
            return new BookingSlotInPastResult(slot.StartUtc);
        }

        // SCRUM-34 AC3. Last of the reads because it is the only one that scans a second table.
        var clash = await _appointmentRepository.FindPatientOverlapAsync(
            patientId, slot.StartUtc, slot.EndUtc, cancellationToken: cancellationToken);

        if (clash is not null)
        {
            return new BookingPatientOverlapResult(clash.AppointmentId, clash.StartUtc, clash.EndUtc);
        }

        slot.Status = SlotStatus.Booked;
        slot.UpdatedBy = actor;

        var appointment = new AppointmentEntity
        {
            SlotId = slot.SlotId,
            PatientId = patientId,
            PatientNumber = patientDetails?.PatientNumber,
            PatientName = patientDetails?.PatientName,
            DoctorId = slot.DoctorId,
            StartUtc = slot.StartUtc,
            EndUtc = slot.EndUtc,
            SlotDate = slot.SlotDate,
            DurationMinutes = slot.DurationMinutes,
            ServiceCode = serviceCode ?? ServiceCodes.GeneralConsultation,
            Status = AppointmentStatus.Booked,
            CreatedBy = actor
        };

        await _appointmentRepository.AddAsync(appointment, cancellationToken);
        await _outboxRepository.AddAsync(
            AppointmentOutboxMessages.Booked(appointment, correlationId, nowUtc),
            cancellationToken);

        // SCRUM-36: every appointment's history starts with the booking that created it.
        await _historyRepository.AddAsync(
            AppointmentHistoryEntry.ForBooking(appointment, actor, nowUtc),
            cancellationToken);

        // One save for the slot mutation, the appointment, the event row and the history entry,
        // inside the caller's transaction, so AC1 and AC4 of SCRUM-34 commit together or not at
        // all — the transactional outbox. A lost race throws out of here and is handled by
        // BookAsync.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new BookingCreatedResult(AppointmentMapping.ToResponse(appointment));
    }

    public async Task<AppointmentResponse?> GetByIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _appointmentRepository.GetByAppointmentIdAsync(
            appointmentId, cancellationToken);

        return appointment is null ? null : AppointmentMapping.ToResponse(appointment);
    }
}
