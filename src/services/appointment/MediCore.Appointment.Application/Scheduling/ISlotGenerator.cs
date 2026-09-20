using MediCore.Appointment.Application.Entities;

namespace MediCore.Appointment.Application.Scheduling;

/// <summary>
/// Turns a <see cref="DoctorSchedule"/> into the set of <see cref="Slot"/> rows it implies over a
/// date range, honouring public holidays and doctor leave (SCRUM-32 AC1 and AC3).
/// </summary>
public interface ISlotGenerator
{
    /// <summary>
    /// Produces the slots <paramref name="schedule"/> implies between <paramref name="from"/> and
    /// <paramref name="to"/> (both inclusive, Asia/Colombo calendar dates).
    /// </summary>
    /// <remarks>
    /// Pure: no database access, no clock reads, no side effects. The returned slots are detached
    /// entities in <see cref="SlotStatus.Available"/> with their audit columns left unset — the
    /// <c>DbContext</c> stamps those on save.
    /// <para>
    /// The range is intersected with the schedule's <see cref="DoctorSchedule.EffectiveFrom"/> and
    /// <see cref="DoctorSchedule.EffectiveTo"/>, so a caller may safely pass a horizon wider than
    /// the schedule's own lifetime.
    /// </para>
    /// <para>
    /// Dates falling on a public holiday, or inside a leave period belonging to this schedule's
    /// doctor, are skipped entirely rather than generated and then removed — AC3 requires that no
    /// slot exist on such a date.
    /// </para>
    /// </remarks>
    /// <param name="schedule">The schedule to expand. Inactive or soft-deleted schedules yield nothing.</param>
    /// <param name="from">First candidate date, inclusive.</param>
    /// <param name="to">Last candidate date, inclusive.</param>
    /// <param name="holidays">Clinic-wide closures. Soft-deleted entries are ignored.</param>
    /// <param name="leaves">
    /// Leave periods. Entries for other doctors and soft-deleted entries are ignored, so callers
    /// may pass an unfiltered set.
    /// </param>
    /// <returns>Slots in chronological order; empty when the schedule produces none.</returns>
    IReadOnlyList<Slot> Generate(
        DoctorSchedule schedule,
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<PublicHoliday> holidays,
        IReadOnlyCollection<DoctorLeave> leaves);

    /// <summary>
    /// The default generation window: today in Colombo through
    /// <see cref="SchedulingOptions.SlotHorizonDays"/> days ahead, both inclusive.
    /// </summary>
    /// <remarks>
    /// This is the only clock-dependent member. <see cref="Generate"/> itself takes an explicit
    /// range so that it stays a pure function of its arguments.
    /// </remarks>
    (DateOnly From, DateOnly To) CurrentHorizon();
}
