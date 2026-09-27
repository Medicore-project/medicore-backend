using MediCore.Appointment.Application.DTOs;
using AppointmentEntity = MediCore.Appointment.Application.Entities.Appointment;

namespace MediCore.Appointment.Application.Services;

/// <summary>Entity-to-DTO mapping shared by the services that return an appointment.</summary>
internal static class AppointmentMapping
{
    public static AppointmentResponse ToResponse(AppointmentEntity appointment) => new(
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
