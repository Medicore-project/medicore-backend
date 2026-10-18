namespace MediCore.Appointment.UnitTests.Support;

/// <summary>
/// A frozen clock built as a Moq <b>stub</b> of the abstract <see cref="TimeProvider"/>.
/// </summary>
/// <remarks>
/// The services never call <c>DateTime.UtcNow</c>; they ask an injected <see cref="TimeProvider"/>.
/// That seam is what makes time-based rules (past slots, the 24-hour cancellation window, waitlist
/// lead time) testable at exact boundaries. Moq can stub it because <c>GetUtcNow</c> is
/// <c>virtual</c> on an abstract class — no interface needed.
/// </remarks>
public static class TestClock
{
    /// <summary>Wednesday 23 Sep 2026, 08:00 UTC (13:30 in Colombo) — inside Sprint 3.</summary>
    public static readonly DateTime Now = new(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc);

    public static TimeProvider At(DateTime utcNow)
    {
        var clock = new Mock<TimeProvider>();
        clock.Setup(c => c.GetUtcNow()).Returns(new DateTimeOffset(utcNow, TimeSpan.Zero));
        return clock.Object;
    }

    public static TimeProvider Frozen() => At(Now);
}
