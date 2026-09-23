namespace MediCore.Patient.Application.Interfaces;

/// <summary>
/// Mints the short-lived token that lets someone who has identified themselves book an appointment
/// without holding an account.
/// </summary>
/// <remarks>
/// <para>
/// Signed with the symmetric <c>Jwt:Key</c> every MediCore service already shares, so the
/// appointment service can validate it with the JWT bearer handler it already has — no call back
/// to this service, no shared table, no introspection endpoint. That matters: cross-service HTTP
/// at request time is the coupling SCRUM-33 was written to remove.
/// </para>
/// <para>
/// The token deliberately carries <strong>no role claim of any kind</strong>. Every authorization
/// policy in both services is role-based, so a principal without one fails all of them; the single
/// exception is the appointment service's <c>BookingCreator</c>, which accepts the
/// <c>patientId</c> claim instead. A leaked token therefore reaches exactly one endpoint, for
/// exactly one patient, for twenty minutes. Adding <c>role: "Patient"</c> "for tidiness" would
/// break that: the Patient role satisfies no policy today, but that is a fact about today's policy
/// table, not an invariant, and a future <c>RequireRole("Patient")</c> would silently widen every
/// token already in the wild.
/// </para>
/// </remarks>
public interface IBookingTokenGenerator
{
    /// <summary>The token and the instant it stops being accepted.</summary>
    (string Token, DateTime ExpiresAtUtc) Generate(Guid patientId);
}
