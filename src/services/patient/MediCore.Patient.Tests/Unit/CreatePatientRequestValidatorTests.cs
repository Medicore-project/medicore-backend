using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Validators;

namespace MediCore.Patient.Tests.Unit;

public sealed class CreatePatientRequestValidatorTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
    private readonly CreatePatientRequestValidator _validator = new(new FixedTimeProvider(Today));

    [Theory]
    [InlineData("123456789V")]
    [InlineData("123456789x")]
    [InlineData("200012345678")]
    public void Valid_nic_formats_are_accepted(string nic)
    {
        var result = _validator.Validate(ValidRequest() with { Nic = nic });

        Assert.DoesNotContain(result.Errors, error => error.PropertyName == nameof(CreatePatientRequest.Nic));
    }

    [Theory]
    [InlineData("12345678V")]
    [InlineData("123456789A")]
    [InlineData("20001234567")]
    [InlineData("ABCDEFGHIJKL")]
    public void Invalid_nic_formats_are_rejected(string nic)
    {
        var result = _validator.Validate(ValidRequest() with { Nic = nic });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreatePatientRequest.Nic));
    }

    [Fact]
    public void Future_date_of_birth_is_rejected()
    {
        var result = _validator.Validate(ValidRequest() with
        {
            DateOfBirth = DateOnly.FromDateTime(Today.UtcDateTime).AddDays(1)
        });

        Assert.Contains(result.Errors, error =>
            error.PropertyName == nameof(CreatePatientRequest.DateOfBirth) &&
            error.ErrorMessage == "Date of birth cannot be in the future.");
    }

    [Fact]
    public void Required_patient_details_are_enforced()
    {
        var result = _validator.Validate(new CreatePatientRequest(
            "", "", "", default, "", "", "", "", null, "", null, null));

        var invalidProperties = result.Errors.Select(error => error.PropertyName).ToHashSet();
        Assert.Contains(nameof(CreatePatientRequest.Nic), invalidProperties);
        Assert.Contains(nameof(CreatePatientRequest.FirstName), invalidProperties);
        Assert.Contains(nameof(CreatePatientRequest.LastName), invalidProperties);
        Assert.Contains(nameof(CreatePatientRequest.DateOfBirth), invalidProperties);
        Assert.Contains(nameof(CreatePatientRequest.Gender), invalidProperties);
        Assert.Contains(nameof(CreatePatientRequest.Email), invalidProperties);
        Assert.Contains(nameof(CreatePatientRequest.Phone), invalidProperties);
        Assert.Contains(nameof(CreatePatientRequest.AddressLine1), invalidProperties);
        Assert.Contains(nameof(CreatePatientRequest.District), invalidProperties);
    }

    [Fact]
    public void Invalid_phone_number_is_rejected()
    {
        var result = _validator.Validate(ValidRequest() with { Phone = "12345" });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreatePatientRequest.Phone));
    }

    private static CreatePatientRequest ValidRequest() => new(
        "200012345678",
        "Nimali",
        "Perera",
        new DateOnly(2000, 5, 15),
        "Female",
        "nimali@example.com",
        "0771234567",
        "12 Hospital Road",
        null,
        "Colombo",
        "Kamal Perera",
        "0711234567");

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
