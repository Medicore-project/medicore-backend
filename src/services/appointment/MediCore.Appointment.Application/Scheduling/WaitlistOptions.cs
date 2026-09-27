namespace MediCore.Appointment.Application.Scheduling;

/// <summary>
/// The waitlist's timings and limits, bound from the <c>Appointments:Waitlist</c> configuration
/// section (SCRUM-37).
/// </summary>
/// <remarks>
/// The raw values are what configuration says; the <c>Effective…</c> members are what the service
/// uses, so a mistyped negative can never make an offer expire before it is made or let a patient
/// join unlimited queues.
/// </remarks>
public sealed class WaitlistOptions
{
    /// <summary>Configuration section name in <c>appsettings.json</c>.</summary>
    public const string SectionName = "Appointments:Waitlist";

    /// <summary>
    /// How long a released slot is held for the patient it is offered to, in minutes. The offer
    /// never outlives the slot's start, whatever this says.
    /// </summary>
    public int OfferWindowMinutes { get; set; } = 60;

    /// <summary>
    /// A slot starting sooner than this is not offered at all — nobody could reasonably answer and
    /// travel in time — and simply returns to the public list.
    /// </summary>
    public int MinimumLeadMinutes { get; set; } = 15;

    /// <summary>
    /// How often the background sweeper expires lapsed offers and passes them on. Zero or less
    /// turns the sweeper off, which test hosts use.
    /// </summary>
    public int SweepIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// How many queues one patient may be waiting in at once. A booking token only needs a patient
    /// number and a date of birth, and every offer holds a slot, so an unlimited count would let one
    /// caller sit on a clinic's cancellations.
    /// </summary>
    public int MaxActiveEntriesPerPatient { get; set; } = 3;

    /// <summary><see cref="OfferWindowMinutes"/>, at least one minute.</summary>
    public TimeSpan EffectiveOfferWindow => TimeSpan.FromMinutes(Math.Max(1, OfferWindowMinutes));

    /// <summary><see cref="MinimumLeadMinutes"/>, never negative.</summary>
    public TimeSpan EffectiveMinimumLead => TimeSpan.FromMinutes(Math.Max(0, MinimumLeadMinutes));

    /// <summary><see cref="MaxActiveEntriesPerPatient"/>, at least one.</summary>
    public int EffectiveMaxActiveEntriesPerPatient => Math.Max(1, MaxActiveEntriesPerPatient);
}
