using FluentValidation.TestHelper;
using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Validators;
using MediCore.Appointment.UnitTests.Support;

namespace MediCore.Appointment.UnitTests.Validation;

/// <summary>
/// Input validation at the API edge — boundary-value analysis and equivalence partitions expressed
/// as parameterized cases, using FluentValidation's own <c>TestValidate</c> helper.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
[Trait(TestCategories.Feature, TestCategories.Validation)]
[Trait(TestCategories.Technique, TestCategories.Parameterized)]
public sealed class AppointmentRequestValidatorTests
{
    private static readonly Guid AnySlot = Guid.Parse("5107a000-0000-0000-0000-0000000000ff");

    // ── Book ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, true)]               // optional: resolved from the doctor anyway
    [InlineData("FREE-VISIT", false)]
    public void Book_accepts_only_known_service_codes(string? serviceCode, bool valid)
    {
        var result = new BookAppointmentRequestValidator()
            .TestValidate(new BookAppointmentRequest(AnySlot, Ids.Patient, serviceCode));

        if (valid)
        {
            result.ShouldNotHaveAnyValidationErrors();
        }
        else
        {
            result.ShouldHaveValidationErrorFor(r => r.ServiceCode);
        }
    }

    [Fact]
    public void Book_requires_a_slot_id()
    {
        new BookAppointmentRequestValidator()
            .TestValidate(new BookAppointmentRequest(Guid.Empty, Ids.Patient, null))
            .ShouldHaveValidationErrorFor(r => r.SlotId);
    }

    // ── Cancel: boundary values around MaxReasonLength = 500 ───────────────

    public static TheoryData<string?, bool> CancelReasons => new()
    {
        { new string('r', 500), true },                 // on the boundary
        { new string('r', 501), false },                // one over
    };

    [Theory]
    [MemberData(nameof(CancelReasons))]
    public void Cancel_requires_a_reason_of_at_most_500_characters(string? reason, bool valid)
    {
        var result = new CancelAppointmentRequestValidator().TestValidate(new CancelAppointmentRequest(reason!));

        Assert.Equal(valid, result.IsValid);
    }

    // ── Complete: boundary values around MaxNotesLength = 8000 ─────────────

    [Theory]
    [InlineData(CompleteAppointmentRequestValidator.MaxNotesLength, true)]
    [InlineData(CompleteAppointmentRequestValidator.MaxNotesLength + 1, false)]
    public void Complete_requires_clinical_notes_within_the_limit(int length, bool valid)
    {
        var result = new CompleteAppointmentRequestValidator()
            .TestValidate(new CompleteAppointmentRequest(new string('n', length)));

        Assert.Equal(valid, result.IsValid);
    }

    // ── Reschedule ───────────────────────────────────────────────────────────

    [Fact]
    public void Reschedule_requires_a_target_slot()
    {
        new RescheduleAppointmentRequestValidator()
            .TestValidate(new RescheduleAppointmentRequest(Guid.Empty))
            .ShouldHaveValidationErrorFor(r => r.NewSlotId);

        new RescheduleAppointmentRequestValidator()
            .TestValidate(new RescheduleAppointmentRequest(AnySlot))
            .ShouldNotHaveAnyValidationErrors();
    }
}
