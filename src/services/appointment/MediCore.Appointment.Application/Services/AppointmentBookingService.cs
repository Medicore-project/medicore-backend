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
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public AppointmentBookingService(
        IAppointmentRepository appointmentRepository,
        ISlotRepository slotRepository,
        IDoctorCacheRepository doctorRepository,
        IOutboxMessageRepository outboxRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _appointmentRepository = appointmentRepository;
        _slotRepository = slotRepository;
        _doctorRepository = doctorRepository;
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<BookingResult> BookAsync(
        Guid slotId,
        Guid patientId,
        string? serviceCode,
        string actor,
        string correlationId,
        BookingPatientDetails? patientDetails = null,
        CancellationToken cancellationToken = default)
    {
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
            patientId, slot.StartUtc, slot.EndUtc, cancellationToken);

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

        try
        {
            // One save for the slot mutation, the appointment and the event row. EF wraps it in a
            // single transaction, so AC1 and AC4 commit together or not at all — the transactional
            // outbox, without needing an explicit transaction anywhere in this service.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (SlotAlreadyBookedException)
        {
            // ux_appointments_slot caught a racer that passed the status check at the same moment
            // we did. The unit of work has cleared the change tracker, taking the slot mutation and
            // the outbox row with it — correct, since nothing committed. Report and stop; this
            // scoped context must not be saved again.
            return new BookingSlotTakenResult();
        }

        return new BookingCreatedResult(ToResponse(appointment));
    }

    public async Task<AppointmentResponse?> GetByIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _appointmentRepository.GetByAppointmentIdAsync(
            appointmentId, cancellationToken);

        return appointment is null ? null : ToResponse(appointment);
    }

    private static AppointmentResponse ToResponse(AppointmentEntity appointment) => new(
        appointment.AppointmentId,
        appointment.PatientId,
        appointment.PatientNumber,
        appointment.PatientName,
        appointment.DoctorId,
        appointment.SlotId,
        appointment.StartUtc,
        appointment.EndUtc,
        appointment.SlotDate,
        appointment.DurationMinutes,
        appointment.ServiceCode,
        appointment.Status,
        appointment.CreatedAt);
}
