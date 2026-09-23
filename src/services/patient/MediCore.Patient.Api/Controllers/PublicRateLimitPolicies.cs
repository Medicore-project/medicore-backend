namespace MediCore.Patient.Api.Controllers;

/// <summary>
/// Rate-limit policy names for the anonymous booking endpoints.
/// </summary>
/// <remarks>
/// <para>
/// These endpoints are the service's only unauthenticated surface, and both are abusable: identify
/// lets someone work through sequential patient numbers, and public-register writes a real patient
/// row plus an outbox event on every call. They therefore get their own, much tighter budget than
/// the service-wide <c>patient-registration</c> policy that <c>MapControllers</c> applies.
/// </para>
/// <para>
/// <strong>Deliberately not partitioned by IP.</strong> The obvious move is
/// <c>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress)</c>, but every
/// request arrives through the YARP gateway and forwarded headers are configured nowhere in this
/// solution, so <c>RemoteIpAddress</c> is the gateway's address for everyone. A per-IP limiter
/// would put the entire internet in one partition and lock the world out after a handful of
/// requests — a worse outage than the abuse it prevents. Making this per-IP means first adding
/// <c>UseForwardedHeaders</c> with the gateway as a known proxy, in both services, which is its
/// own change with its own trust decisions. Recorded in the ticket's open items.
/// </para>
/// </remarks>
public static class PublicRateLimitPolicies
{
    /// <summary>
    /// Covers both anonymous endpoints. Generous enough that a family booking together at a
    /// clinic kiosk is never refused, small enough that scripted enumeration is slow and loud.
    /// </summary>
    public const string PublicBooking = "patient-public-booking";

    public const int PermitLimit = 20;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
