using MediCore.Appointment.Application.DTOs;
using MediCore.Appointment.Application.Entities;
using MediCore.Appointment.Application.Validators;

namespace MediCore.Appointment.Tests.Unit;

public sealed class AppointmentValidatorTests
{
    private readonly BookAppointmentRequestValidator _validator = new();

    [Fact]
    public void A_booking_must_name_a_slot()
    {
        var result = _validator.Validate(new BookAppointmentRequest(Guid.Empty, Guid.NewGuid(), null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(BookAppointmentRequest.SlotId));
    }

    [Fact]
    public void An_omitted_service_code_is_fine_because_it_defaults()
    {
        var result = _validator.Validate(new BookAppointmentRequest(Guid.NewGuid(), Guid.NewGuid(), null));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(ServiceCodes.GeneralConsultation)]
    [InlineData(ServiceCodes.SpecialistConsultation)]
    [InlineData(ServiceCodes.FollowUp)]
    public void Every_known_service_code_is_accepted(string code)
    {
        var result = _validator.Validate(new BookAppointmentRequest(Guid.NewGuid(), Guid.NewGuid(), code));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("NOPE")]
    [InlineData("")]
    [InlineData("gen-consult")]
    public void An_unknown_service_code_is_refused_and_the_list_is_offered(string code)
    {
        var result = _validator.Validate(new BookAppointmentRequest(Guid.NewGuid(), Guid.NewGuid(), code));

        Assert.False(result.IsValid);
        var error = Assert.Single(
            result.Errors,
            e => e.PropertyName == nameof(BookAppointmentRequest.ServiceCode));
        Assert.Contains(ServiceCodes.GeneralConsultation, error.ErrorMessage);
    }

    [Fact]
    public void An_empty_patient_id_passes_here_because_a_booking_token_supplies_it()
    {
        // The claim is resolved in the controller, so the body legitimately omits a patient on the
        // public path. The controller refuses the request when neither source produced one.
        var result = _validator.Validate(new BookAppointmentRequest(Guid.NewGuid(), Guid.Empty, null));

        Assert.True(result.IsValid);
    }

    // ── SCRUM-36: cancel ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_cancellation_needs_a_reason(string? reason)
    {
        var result = new CancelAppointmentRequestValidator().Validate(new CancelAppointmentRequest(reason!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CancelAppointmentRequest.Reason));
    }

    [Fact]
    public void A_reason_may_fill_the_history_column_but_not_overflow_it()
    {
        var validator = new CancelAppointmentRequestValidator();
        var longest = new string('x', CancelAppointmentRequestValidator.MaxReasonLength);

        Assert.True(validator.Validate(new CancelAppointmentRequest(longest)).IsValid);
        Assert.False(validator.Validate(new CancelAppointmentRequest(longest + "x")).IsValid);
    }

    [Fact]
    public void Surrounding_spaces_do_not_count_against_the_limit()
    {
        // The service trims before storing, so the limit applies to what is stored.
        var padded = "  " + new string('x', CancelAppointmentRequestValidator.MaxReasonLength) + "  ";

        Assert.True(new CancelAppointmentRequestValidator().Validate(new CancelAppointmentRequest(padded)).IsValid);
    }

    [Fact]
    public void The_reason_limit_matches_the_history_column()
    {
        Assert.Equal(500, CancelAppointmentRequestValidator.MaxReasonLength);
    }
}
