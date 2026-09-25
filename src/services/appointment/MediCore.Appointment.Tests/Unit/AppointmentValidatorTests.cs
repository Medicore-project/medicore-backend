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
}
