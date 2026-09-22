namespace MediCore.Appointment.Application.DTOs;

// ── Responses ─────────────────────────────────────────────────────────────────

/// <summary>A bookable doctor, as this service's cache of Identity's staff data knows them.</summary>
/// <remarks>
/// Read from the local cache, not from Identity, so it answers while Identity is down. The price is
/// that a change made in Identity shows here only once its staff event has been consumed.
/// </remarks>
/// <param name="DoctorId">
/// The doctor's staff id — the value to pass as <c>doctorId</c> to schedules, slots and leave.
/// </param>
/// <param name="Specialization">Free text as entered in Identity; empty when none is set.</param>
/// <param name="DepartmentId">Identity's department id. Identity's events carry no department name.</param>
public sealed record DoctorResponse(
    Guid DoctorId,
    string FullName,
    string Specialization,
    Guid DepartmentId);
