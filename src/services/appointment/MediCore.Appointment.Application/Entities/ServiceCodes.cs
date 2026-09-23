namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// The billable service a booking represents, carried to the billing service on
/// <c>appointment.booked</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>These are placeholders.</strong> There is no billing service, no service-code catalogue
/// and no price anywhere in the system yet — billing arrives in Sprint 4, and the booking screens
/// say so out loud. The contract's <c>AppointmentBookedEvent.ServiceCode</c> is a required string,
/// so this list exists to keep the event meaningful and the column readable until the billing team
/// replaces it with a real catalogue (most likely a table, at which point this class goes away).
/// </para>
/// <para>
/// String constants rather than a C# enum, matching <see cref="SlotStatus"/> and
/// <see cref="AppointmentStatus"/>: the column stays human-readable and adding a code is not a
/// migration.
/// </para>
/// </remarks>
public static class ServiceCodes
{
    /// <summary>A standard consultation. The default when a booking does not state a code.</summary>
    public const string GeneralConsultation = "GEN-CONSULT";

    /// <summary>A consultation with a doctor in a named specialization.</summary>
    public const string SpecialistConsultation = "SPEC-CONSULT";

    /// <summary>A return visit following an earlier consultation.</summary>
    public const string FollowUp = "FOLLOW-UP";

    /// <summary>Every code a booking may carry.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        GeneralConsultation,
        SpecialistConsultation,
        FollowUp
    ];

    /// <summary>
    /// Whether <paramref name="code"/> is one this service recognises. Case-sensitive on purpose:
    /// the code is stored verbatim and published verbatim, so accepting "gen-consult" would put two
    /// spellings of one service on the topic.
    /// </summary>
    public static bool IsKnown(string? code) =>
        code is not null && All.Contains(code, StringComparer.Ordinal);
}
