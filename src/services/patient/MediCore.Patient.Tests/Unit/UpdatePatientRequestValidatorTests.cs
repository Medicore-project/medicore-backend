using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Validators;

namespace MediCore.Patient.Tests.Unit;

public sealed class UpdatePatientRequestValidatorTests
{
    private static readonly DateTimeOffset Today = new(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
    private readonly UpdatePatientRequestValidator _validator = new(new FixedTimeProvider(Today));

    [Fact]
    public void Valid_update_is_accepted()
    {
        Assert.True(_validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Future_date_of_birth_is_rejected()
    {
        var result = _validator.Validate(ValidRequest() with { DateOfBirth = new DateOnly(2026, 9, 15) });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdatePatientRequest.DateOfBirth));
    }

    [Fact]
    public void Invalid_contact_details_are_rejected()
    {
        var result = _validator.Validate(ValidRequest() with { Email = "invalid", Phone = "123" });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdatePatientRequest.Email));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdatePatientRequest.Phone));
    }

    private static UpdatePatientRequest ValidRequest() => new(
        "Nimali", "Perera", new DateOnly(2000, 5, 15), "Female",
        "nimali@example.com", "0771234567", "12 Hospital Road", null,
        "Colombo", null, null);

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
