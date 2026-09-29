namespace MediCore.Appointment.Api.Controllers;

/// <summary>
/// The service-wide fixed-window rate limit that <c>MapControllers</c> applies to every endpoint,
/// bound from <c>RateLimiting:AppointmentDefault</c>.
/// </summary>
/// <remarks>
/// SCRUM-35. The defaults are the limits the service always had, so nothing changes unless
/// configuration says so. They are configurable for one reason: the concurrent-booking load test
/// fires far more than 100 requests a minute at <c>POST /api/appointments</c>, and at the default
/// limit it would measure 429s instead of the 409s it exists to count. The load test raises the
/// limit through <c>docker-compose.loadtest.yml</c>, for that run only
/// (<c>RateLimiting__AppointmentDefault__PermitLimit</c>).
/// </remarks>
public sealed class AppointmentRateLimitOptions
{
    public const string SectionName = "RateLimiting:AppointmentDefault";

    /// <summary>The policy name the controllers are mapped with.</summary>
    public const string PolicyName = "appointment-default";

    /// <summary>Requests allowed per window, across all callers — the limiter is not partitioned.</summary>
    public int PermitLimit { get; set; } = 100;

    public int WindowSeconds { get; set; } = 60;
}
