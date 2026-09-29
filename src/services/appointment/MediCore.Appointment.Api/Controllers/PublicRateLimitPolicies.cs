namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// Rate-limit policy names for the anonymous booking reads.
/// </summary>
/// <remarks>
/// <para>
/// These are the service's only unauthenticated endpoints and they are cheap to scrape: doctors,
/// specializations and a whole horizon of free slots. They therefore get their own budget on top of
/// the service-wide <c>appointment-default</c> policy that <c>MapControllers</c> applies, so a
/// scraper exhausts this one rather than the window every receptionist in the building shares.
/// </para>
/// <para>
/// <strong>Deliberately not partitioned by IP</strong>, for the same reason as the Patient
/// service's equivalent: every request arrives through the YARP gateway and forwarded headers are
/// configured nowhere in this solution, so <c>RemoteIpAddress</c> is the gateway's address for
/// everyone. A per-IP limiter would put the entire internet in a single partition and lock
/// everybody out — a worse outage than the scraping it prevents. Doing it properly means adding
/// <c>UseForwardedHeaders</c> with the gateway as a known proxy, in both services, which is its own
/// change with its own trust decisions. Recorded in the ticket's open items.
/// </para>
/// </remarks>
public static class PublicRateLimitPolicies
{
    /// <summary>
    /// Covers every anonymous read. Generous enough for someone browsing several doctors and weeks
    /// before choosing, small enough that bulk scraping is slow and visible.
    /// </summary>
    public const string PublicBooking = "appointment-public-booking";

    public const int PermitLimit = 60;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
