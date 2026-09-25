namespace MediCore.Appointment.Application.DTOs;

/// <summary>
/// A doctor as the public booking page sees them.
/// </summary>
/// <remarks>
/// A deliberately reduced <see cref="DoctorResponse"/>: name and specialization are what the clinic
/// already advertises, and nothing more is published. <c>DepartmentId</c> is dropped — it is an
/// internal identifier of no use to a patient and no reason to publish the org chart.
/// <para>
/// Anything added here becomes visible to the whole internet, since the gateway does not
/// authenticate. <c>PublicBookingContractTests</c> fails if this record grows a field.
/// </para>
/// </remarks>
public sealed record PublicDoctorResponse(
    Guid DoctorId,
    string FullName,
    string Specialization);

/// <summary>
/// A bookable time as the public booking page sees it.
/// </summary>
/// <remarks>
/// A deliberately reduced <see cref="SlotResponse"/>. Four fields are left out on purpose:
/// <list type="bullet">
/// <item><c>FlaggedReason</c> and <c>FlaggedAtUtc</c> — free text a staff member wrote about
/// <em>another patient's</em> stranded booking. This is the most important omission on the
/// page.</item>
/// <item><c>Status</c> — always "Available" here, so it carries no information.</item>
/// <item><c>ScheduleId</c> and <c>DoctorId</c> — internal identifiers the caller already knows or
/// has no use for; the doctor was chosen to get this list.</item>
/// </list>
/// </remarks>
public sealed record PublicSlotResponse(
    Guid SlotId,
    DateTime StartUtc,
    DateTime EndUtc,
    DateOnly SlotDate,
    int DurationMinutes);
