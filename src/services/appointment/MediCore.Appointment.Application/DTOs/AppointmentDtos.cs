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

/// <summary>
/// One appointment as clinic staff see it in the booking grid and the appointments list — who it
/// is for, with whom, and when.
/// </summary>
/// <param name="PatientNumber">Null for a booking made with a bare patient id; see the entity.</param>
/// <param name="DoctorName">Null only when the doctor cache has never heard of the doctor.</param>
public sealed record AppointmentSummaryResponse(
    Guid AppointmentId,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    Guid DoctorId,
    string? DoctorName,
    string? Specialization,
    Guid SlotId,
    DateTime StartUtc,
    DateTime EndUtc,
    DateOnly SlotDate,
    int DurationMinutes,
    string ServiceCode,
    string Status,
    DateTime CreatedAt);

/// <summary>One of the caller's own upcoming appointments, for the public booking page.</summary>
/// <remarks>
/// Reduced on purpose, like the other public DTOs: no patient id, no slot id, no audit columns. The
/// caller already knows who they are; what they need is with whom and when.
/// </remarks>
public sealed record PatientAppointmentResponse(
    Guid AppointmentId,
    string? DoctorName,
    string? Specialization,
    DateTime StartUtc,
    DateTime EndUtc,
    DateOnly SlotDate,
    int DurationMinutes,
    string ServiceCode,
    string Status);
