using System.Text;

namespace MediCore.Appointment.Application.Entities;

/// <summary>
/// The billable service a booking represents, carried to the billing service on
/// <c>appointment.booked</c>, where it is the key the tariff is looked up by.
/// </summary>
/// <remarks>
/// <para>
/// A booking's code follows from its doctor and from nothing else. A doctor with no specialization
/// is a <see cref="GeneralConsultation"/>. A doctor with one gets that specialization's own code —
/// <c>SPEC-CARDIOLOGY</c>, <c>SPEC-EAR-NOSE-THROAT</c> — so billing can price each specialization
/// separately. The billing service's tariff catalogue builds its codes from the same specialization
/// names by the same rule (see <see cref="ForSpecialization"/>), which is the whole of the link
/// between a tariff and a specialization: no id crosses the service boundary.
/// </para>
/// <para>
/// String constants rather than a C# enum, matching <see cref="SlotStatus"/> and
/// <see cref="AppointmentStatus"/>: the column stays human-readable and adding a specialization is
/// not a migration.
/// </para>
/// </remarks>
public static class ServiceCodes
{
    /// <summary>A consultation with a doctor who has no specialization.</summary>
    public const string GeneralConsultation = "GEN-CONSULT";

    /// <summary>
    /// The default specialist code. Billing prices a specialization that has no tariff of its own
    /// at this one, and it is what a specialization name with nothing usable in it resolves to.
    /// </summary>
    public const string SpecialistConsultation = "SPEC-CONSULT";

    /// <summary>What every specialization's code starts with.</summary>
    public const string SpecializationPrefix = "SPEC-";

    /// <summary>The width of the <c>ServiceCode</c> columns here and in billing.</summary>
    public const int MaxLength = 50;

    /// <summary>
    /// Resolves the billable service for a doctor-owned booking from the trusted doctor cache,
    /// never from the caller.
    /// </summary>
    public static string ResolveForDoctor(string? specialization) =>
        string.IsNullOrWhiteSpace(specialization)
            ? GeneralConsultation
            : ForSpecialization(specialization);

    /// <summary>
    /// The code for one specialization: its name upper-cased, with every run of anything other
    /// than an ASCII letter or digit collapsed to a single hyphen, behind
    /// <see cref="SpecializationPrefix"/>.
    /// </summary>
    /// <remarks>
    /// Specialization is free text in Identity, so "Cardiology", "cardiology " and "CARDIOLOGY" must
    /// all land on one tariff; and the result has to satisfy billing's tariff-code rule (letters,
    /// digits and single hyphens, at most <see cref="MaxLength"/> characters). The tariff form in
    /// the frontend applies this same rule — change one and the other must follow, or a tariff
    /// stops matching the bookings it was created for.
    /// </remarks>
    public static string ForSpecialization(string specialization)
    {
        var slug = new StringBuilder(specialization.Length);

        foreach (var character in specialization)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                slug.Append(char.ToUpperInvariant(character));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var maxSlugLength = MaxLength - SpecializationPrefix.Length;
        if (slug.Length > maxSlugLength)
        {
            slug.Length = maxSlugLength;
        }

        var trimmed = slug.ToString().TrimEnd('-');

        // A name with no ASCII letter or digit in it still belongs to a specialist.
        return trimmed.Length == 0 ? SpecialistConsultation : SpecializationPrefix + trimmed;
    }

    /// <summary>
    /// Whether <paramref name="code"/> is one a booking can carry: the general consultation, or a
    /// well-formed specialization code. Case-sensitive on purpose: the code is stored verbatim and
    /// published verbatim, so accepting "gen-consult" would put two spellings of one service on the
    /// topic.
    /// </summary>
    public static bool IsKnown(string? code) =>
        code is not null
        && (string.Equals(code, GeneralConsultation, StringComparison.Ordinal)
            || IsSpecializationCode(code));

    /// <summary>
    /// Well-formed means exactly what <see cref="ForSpecialization"/> would have produced, so the
    /// two can never disagree about what a specialization code looks like.
    /// </summary>
    private static bool IsSpecializationCode(string code) =>
        code.StartsWith(SpecializationPrefix, StringComparison.Ordinal)
        && string.Equals(
            code,
            ForSpecialization(code[SpecializationPrefix.Length..]),
            StringComparison.Ordinal);
}
