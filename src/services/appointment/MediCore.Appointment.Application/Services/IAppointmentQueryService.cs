using MediCore.Appointment.Application.DTOs;

namespace MediCore.Appointment.Application.Services;

/// <summary>Reading back what has been booked — for clinic staff, and for a patient's own bookings.</summary>
/// <remarks>
/// Separate from <see cref="IAppointmentBookingService"/> because the two share nothing but the
/// table: booking is a guarded write in one transaction, these are plain reads.
/// </remarks>
public interface IAppointmentQueryService
{
    /// <summary>
    /// Appointments on the Colombo dates <paramref name="from"/>..<paramref name="to"/> inclusive,
    /// for one doctor or the whole clinic, every status. The range is validated by the caller.
    /// </summary>
    Task<IReadOnlyList<AppointmentSummaryResponse>> ListAsync(
        Guid? doctorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    /// <summary>The patient's own appointments that are still booked and yet to start.</summary>
    Task<IReadOnlyList<PatientAppointmentResponse>> ListUpcomingForPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);
}
