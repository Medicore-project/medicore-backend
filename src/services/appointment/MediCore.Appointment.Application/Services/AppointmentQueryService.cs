using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Interfaces;

namespace MediCore.Appointment.Application.Services;

/// <inheritdoc cref="IAppointmentQueryService"/>
public sealed class AppointmentQueryService : IAppointmentQueryService
{
    /// <summary>
    /// More upcoming bookings than any real patient holds. A cap rather than paging, because the
    /// public page shows them in one short list above the booking flow.
    /// </summary>
    public const int UpcomingLimit = 20;

    private readonly IAppointmentRepository _repository;
    private readonly TimeProvider _timeProvider;

    public AppointmentQueryService(IAppointmentRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<AppointmentSummaryResponse>> ListAsync(
        Guid? doctorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var listings = await _repository.ListAsync(doctorId, from, to, cancellationToken);

        return listings.Select(listing => new AppointmentSummaryResponse(
                listing.Appointment.AppointmentId,
                listing.Appointment.PatientId,
                listing.Appointment.PatientNumber,
                listing.Appointment.PatientName,
                listing.Appointment.DoctorId,
                listing.DoctorName,
                listing.DoctorSpecialization,
                listing.Appointment.SlotId,
                listing.Appointment.StartUtc,
                listing.Appointment.EndUtc,
                listing.Appointment.SlotDate,
                listing.Appointment.DurationMinutes,
                listing.Appointment.ServiceCode,
                listing.Appointment.Status,
                listing.Appointment.CreatedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<PatientAppointmentResponse>> ListUpcomingForPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var listings = await _repository.ListUpcomingForPatientAsync(
            patientId,
            _timeProvider.GetUtcNow().UtcDateTime,
            UpcomingLimit,
            cancellationToken);

        return listings.Select(listing => new PatientAppointmentResponse(
                listing.Appointment.AppointmentId,
                listing.DoctorName,
                listing.DoctorSpecialization,
                listing.Appointment.StartUtc,
                listing.Appointment.EndUtc,
                listing.Appointment.SlotDate,
                listing.Appointment.DurationMinutes,
                listing.Appointment.ServiceCode,
                listing.Appointment.Status))
            .ToList();
    }
}
