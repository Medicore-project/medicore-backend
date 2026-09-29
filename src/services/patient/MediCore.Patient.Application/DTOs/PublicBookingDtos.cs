namespace MediCore.Patient.Application.DTOs;

/// <summary>
/// A returning patient proving who they are without a login.
/// </summary>
/// <remarks>
/// The date of birth is not decoration. Patient numbers come from a database sequence — PAT-000001,
/// PAT-000002 — so the number alone is trivially guessable and would let anyone book, or discover a
/// name, by counting upwards.
/// </remarks>
public sealed record IdentifyPatientRequest(string PatientNumber, DateOnly DateOfBirth);

/// <summary>
/// Who the patient is, plus a short-lived token that authorises booking for exactly them.
/// </summary>
/// <param name="BookingToken">
/// A bearer token for <c>POST /appointment/api/appointments</c> and nothing else. It carries no
/// role, so it satisfies no other policy in the system.
/// </param>
public sealed record BookingIdentityResponse(
    Guid PatientId,
    string PatientNumber,
    string FullName,
    string BookingToken,
    DateTime ExpiresAtUtc);
