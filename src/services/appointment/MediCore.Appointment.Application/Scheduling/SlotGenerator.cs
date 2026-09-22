using MediCore.Appointment.Application.Entities;
using Microsoft.Extensions.Options;

namespace MediCore.Appointment.Application.Scheduling;

/// <inheritdoc cref="ISlotGenerator"/>
public sealed class SlotGenerator : ISlotGenerator
{
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<SchedulingOptions> _options;

    public SlotGenerator(TimeProvider timeProvider, IOptions<SchedulingOptions> options)
    {
        _timeProvider = timeProvider;
        _options = options;
    }

    /// <inheritdoc />
    public (DateOnly From, DateOnly To) CurrentHorizon()
    {
        var today = ColomboTime.Today(_timeProvider);
        return (today, today.AddDays(_options.Value.SlotHorizonDays));
    }

    /// <inheritdoc />
    public IReadOnlyList<Slot> Generate(
        DoctorSchedule schedule,
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<PublicHoliday> holidays,
        IReadOnlyCollection<DoctorLeave> leaves)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(holidays);
        ArgumentNullException.ThrowIfNull(leaves);

        // ── Schedule-level gates ─────────────────────────────────────────────
        // A paused or soft-deleted schedule generates nothing. Checked before any arithmetic so a
        // malformed-but-inactive row can never produce slots.
        if (schedule.IsDeleted || !schedule.IsActive)
        {
            return Array.Empty<Slot>();
        }

        // Guards the division below. Validation rejects anything but 15 or 30 before persistence,
        // but the generator must not divide by zero if it is ever handed an unvalidated schedule.
        if (schedule.SlotDurationMinutes <= 0)
        {
            return Array.Empty<Slot>();
        }

        // Subtract through ToTimeSpan() rather than using TimeOnly's own operator: that operator
        // returns the *elapsed* time and wraps around midnight, so an inverted window such as
        // 17:00-09:00 would come back as a positive 16 hours instead of a negative span. Going via
        // TimeSpan yields a genuinely signed difference, so an inverted window falls out below.
        var windowMinutes = (int)(schedule.EndTime.ToTimeSpan() - schedule.StartTime.ToTimeSpan()).TotalMinutes;

        // Lenient fit (decision 7): whole slots only, trailing remainder discarded. Integer
        // division floors, so 09:00-17:20 at 30 minutes yields 16 slots and drops the last 20
        // minutes. This also covers an inverted or too-short window — both give zero slots.
        if (windowMinutes < schedule.SlotDurationMinutes)
        {
            return Array.Empty<Slot>();
        }

        var slotsPerDay = windowMinutes / schedule.SlotDurationMinutes;

        // ── Clip the requested range to the schedule's effective dates ───────
        var start = Later(from, schedule.EffectiveFrom);
        var end = schedule.EffectiveTo is { } effectiveTo ? Earlier(to, effectiveTo) : to;

        if (start > end)
        {
            return Array.Empty<Slot>();
        }

        // ── Exception lookups ────────────────────────────────────────────────
        // Both sets are filtered defensively so the generator is correct regardless of what the
        // caller passes; leave belonging to another doctor is irrelevant to this schedule.
        var holidayDates = holidays
            .Where(holiday => !holiday.IsDeleted)
            .Select(holiday => holiday.Date)
            .ToHashSet();

        // Only *approved* leave suppresses slots. A pending request must leave the calendar alone,
        // otherwise a doctor could clear their own diary just by asking and the administrator's
        // approval would decide nothing.
        var doctorLeave = leaves
            .Where(leave => !leave.IsDeleted
                && leave.DoctorId == schedule.DoctorId
                && LeaveStatus.SuppressesSlots(leave.Status))
            .ToArray();

        // ── Expansion ────────────────────────────────────────────────────────
        var slots = new List<Slot>();

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek != schedule.DayOfWeek)
            {
                continue;
            }

            if (holidayDates.Contains(date) || IsOnLeave(doctorLeave, date))
            {
                continue;
            }

            // The working window is expressed as Colombo wall-clock time; slots are stored as UTC
            // instants. Because the offset is fixed, adding minutes to the UTC instant advances the
            // wall clock by the same amount, so the day can be walked entirely in UTC.
            var dayStartUtc = ColomboTime.ToUtc(date, schedule.StartTime);

            for (var index = 0; index < slotsPerDay; index++)
            {
                var startUtc = dayStartUtc.AddMinutes(index * schedule.SlotDurationMinutes);

                slots.Add(new Slot
                {
                    DoctorId = schedule.DoctorId,
                    DoctorScheduleId = schedule.Id,
                    StartUtc = startUtc,
                    EndUtc = startUtc.AddMinutes(schedule.SlotDurationMinutes),

                    // Derived from StartUtc rather than reusing `date` so the denormalised column
                    // is guaranteed to agree with the instant it summarises. Nothing in the
                    // database enforces that invariant, so it is enforced here.
                    SlotDate = ColomboTime.ToColomboDate(startUtc),

                    DurationMinutes = schedule.SlotDurationMinutes,
                    Status = SlotStatus.Available,
                });
            }
        }

        return slots;
    }

    /// <summary>Whether <paramref name="date"/> falls inside any leave period (both ends inclusive).</summary>
    private static bool IsOnLeave(IReadOnlyList<DoctorLeave> leaves, DateOnly date)
    {
        foreach (var leave in leaves)
        {
            if (leave.StartDate <= date && date <= leave.EndDate)
            {
                return true;
            }
        }

        return false;
    }

    private static DateOnly Later(DateOnly left, DateOnly right) => left > right ? left : right;

    private static DateOnly Earlier(DateOnly left, DateOnly right) => left < right ? left : right;
}
