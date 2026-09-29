using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Messaging;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Application.Services;

/// <summary>
/// The write half of a booking: takes the slot, creates the appointment, and stages its
/// <c>appointment.booked</c> event and first history entry. Shared by booking and by accepting a
/// waitlist offer (SCRUM-37), so an appointment reached either way is indistinguishable downstream —
/// the same row shape, the same event, the same history.
/// </summary>
/// <remarks>
/// Checks nothing and saves nothing. Each caller has already locked what it needs and decided the
/// slot may be taken — booking wants it <see cref="SlotStatus.Available"/>, an accepted offer wants
/// it <see cref="SlotStatus.Offered"/> — and saves once, inside its own transaction, so the
/// transactional outbox holds.
/// </remarks>
public static class AppointmentCreation
{
    public static async Task<AppointmentEntity> CreateAsync(
        Slot slot,
        Guid patientId,
        BookingPatientDetails? patientDetails,
        string? serviceCode,
        string actor,
        string correlationId,
        DateTime nowUtc,
        IAppointmentRepository appointmentRepository,
        IOutboxMessageRepository outboxRepository,
        IAppointmentHistoryRepository historyRepository,
        CancellationToken cancellationToken)
    {
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

        await appointmentRepository.AddAsync(appointment, cancellationToken);
        await outboxRepository.AddAsync(
            AppointmentOutboxMessages.Booked(appointment, correlationId, nowUtc),
            cancellationToken);

        // SCRUM-36: every appointment's history starts with the booking that created it.
        await historyRepository.AddAsync(
            AppointmentHistoryEntry.ForBooking(appointment, actor, nowUtc),
            cancellationToken);

        return appointment;
    }
}
