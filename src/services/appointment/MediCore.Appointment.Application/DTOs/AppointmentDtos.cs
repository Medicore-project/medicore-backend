namespace MediCore.Appointment.Application.DTOs;

/// <summary>
/// Books one slot for one patient.
/// </summary>
/// <param name="SlotId">The slot's business key, from the availability listing.</param>
/// <param name="PatientId">
/// Who the visit is for. <strong>Ignored when the caller presents a booking token</strong>, which
/// names exactly one patient in its <c>patientId</c> claim — only staff booking on someone's behalf
/// ever supply this.
/// </param>
/// <param name="ServiceCode">
/// What the visit will be billed as, one of the <c>ServiceCodes</c> constants. Optional; omitting
/// it means a general consultation.
/// </param>
public sealed record BookAppointmentRequest(Guid SlotId, Guid PatientId, string? ServiceCode);

/// <summary>
/// Who the booking is for, in the form a person recognises — copied onto the appointment so staff
/// can see who booked. Both parts are null when the caller had nothing trustworthy to supply.
/// </summary>
public sealed record BookingPatientDetails(string? PatientNumber, string? PatientName)
{
    public static readonly BookingPatientDetails None = new(null, null);
}

/// <summary>A confirmed booking.</summary>
public sealed record AppointmentResponse(
    Guid AppointmentId,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    Guid DoctorId,
    Guid SlotId,
    DateTime StartUtc,
    DateTime EndUtc,
    DateOnly SlotDate,
    int DurationMinutes,
    string ServiceCode,
    string Status,
    DateTime CreatedAt);
