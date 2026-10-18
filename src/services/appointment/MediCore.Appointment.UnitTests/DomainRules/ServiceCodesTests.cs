using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.UnitTests.Support;

namespace MediCore.Appointment.UnitTests.DomainRules;

/// <summary>
/// The service code on every booking decides what Billing charges, so the slug rules are tested as
/// a table of inputs and expected outputs rather than one example at a time.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
[Trait(TestCategories.Feature, TestCategories.DomainRules)]
[Trait(TestCategories.Technique, TestCategories.Parameterized)]
public sealed class ServiceCodesTests
{
    [Theory]
    [InlineData("Cardiology", "SPEC-CARDIOLOGY")]
    [InlineData("cardiology", "SPEC-CARDIOLOGY")]              // case-insensitive input
    [InlineData("Ear, Nose & Throat", "SPEC-EAR-NOSE-THROAT")] // punctuation runs collapse to one dash
    [InlineData("  Paediatrics!  ", "SPEC-PAEDIATRICS")]       // no leading or trailing dash
    [InlineData("Obstetrics/Gynaecology", "SPEC-OBSTETRICS-GYNAECOLOGY")]
    [InlineData("Café", "SPEC-CAF")]                           // non-ASCII letters are dropped
    [InlineData("???", "SPEC-CONSULT")]                        // nothing usable -> generic specialist
    [InlineData(null, "GEN-CONSULT")]
    [InlineData("", "GEN-CONSULT")]
    [InlineData("   ", "GEN-CONSULT")]
    public void ResolveForDoctor_turns_a_free_text_specialization_into_a_billing_code(
        string? specialization, string expected)
    {
        Assert.Equal(expected, ServiceCodes.ResolveForDoctor(specialization));
    }

    public static IEnumerable<object[]> OverlongSpecializations()
    {
        yield return new object[] { new string('A', 45) }; // exactly fills the code
        yield return new object[] { new string('A', 46) }; // one over
        yield return new object[] { new string('B', 200) };
        yield return new object[] { new string('C', 44) + " Z" }; // truncation would leave a dangling dash
    }

    [Theory]
    [MemberData(nameof(OverlongSpecializations))]
    public void A_code_never_exceeds_the_column_length_or_ends_in_a_dash(string specialization)
    {
        var code = ServiceCodes.ForSpecialization(specialization);

        Assert.True(code.Length <= ServiceCodes.MaxLength, $"'{code}' is {code.Length} chars");
        Assert.StartsWith(ServiceCodes.SpecializationPrefix, code);
        Assert.False(code.EndsWith('-'), $"'{code}' ends with a dash");
    }

    [Theory]
    [InlineData("GEN-CONSULT", true)]
    [InlineData("SPEC-CARDIOLOGY", true)]
    [InlineData("SPEC-CONSULT", true)]
    [InlineData("SPEC-EAR-NOSE-THROAT", true)]
    [InlineData("gen-consult", false)]     // ordinal, case-sensitive
    [InlineData("SPEC-cardiology", false)] // not in canonical form
    [InlineData("SPEC-", false)]
    [InlineData("SPEC--CARDIOLOGY", false)]
    [InlineData("CARDIOLOGY", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKnown_accepts_only_canonical_codes(string? code, bool expected)
    {
        Assert.Equal(expected, ServiceCodes.IsKnown(code));
    }
}
