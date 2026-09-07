using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Validators;

namespace MediCore.Identity.Tests.Unit;

public sealed class StaffValidatorTests
{
    private static readonly CreateStaffRequestValidator CreateValidator = new();
    private static readonly UpdateStaffRequestValidator UpdateValidator = new();

    private static CreateStaffRequest ValidCreateRequest() => new(
        Email: "new.doctor@medicore.local",
        Password: "Password123",
        Role: "Doctor",
        FirstName: "Jane",
        LastName: "Doe",
        Phone: "0771234567",
        Specialization: "Cardiology",
        DepartmentId: Guid.NewGuid());

    [Fact]
    public void Valid_create_request_passes()
    {
        var result = CreateValidator.Validate(ValidCreateRequest());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing-at-sign.com")]
    public void Create_rejects_invalid_email(string email)
    {
        var request = ValidCreateRequest() with { Email = email };

        var result = CreateValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateStaffRequest.Email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short1")]
    [InlineData("1234567")]
    public void Create_rejects_password_shorter_than_8_characters(string password)
    {
        var request = ValidCreateRequest() with { Password = password };

        var result = CreateValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateStaffRequest.Password));
    }

    [Theory]
    [InlineData("")]
    [InlineData("SuperAdmin")]
    [InlineData("doctor")] // case-sensitive: must match seeded role names exactly
    public void Create_rejects_role_not_in_the_allowed_list(string role)
    {
        var request = ValidCreateRequest() with { Role = role };

        var result = CreateValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateStaffRequest.Role));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Doctor")]
    [InlineData("Nurse")]
    [InlineData("Receptionist")]
    [InlineData("Patient")]
    public void Create_accepts_every_seeded_role(string role)
    {
        var request = ValidCreateRequest() with { Role = role };

        var result = CreateValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_rejects_missing_first_name()
    {
        var request = ValidCreateRequest() with { FirstName = "" };

        var result = CreateValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateStaffRequest.FirstName));
    }

    [Fact]
    public void Create_rejects_missing_last_name()
    {
        var request = ValidCreateRequest() with { LastName = "" };

        var result = CreateValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateStaffRequest.LastName));
    }

    [Fact]
    public void Create_rejects_empty_department_id()
    {
        var request = ValidCreateRequest() with { DepartmentId = Guid.Empty };

        var result = CreateValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateStaffRequest.DepartmentId));
    }

    [Fact]
    public void Update_rejects_missing_names_and_department()
    {
        var request = new UpdateStaffRequest(
            FirstName: "",
            LastName: "",
            Phone: null,
            Specialization: null,
            DepartmentId: Guid.Empty,
            IsActive: true);

        var result = UpdateValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateStaffRequest.FirstName));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateStaffRequest.LastName));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateStaffRequest.DepartmentId));
    }

    [Fact]
    public void Update_accepts_a_fully_populated_request()
    {
        var request = new UpdateStaffRequest(
            FirstName: "Jane",
            LastName: "Doe",
            Phone: "0771234567",
            Specialization: "Cardiology",
            DepartmentId: Guid.NewGuid(),
            IsActive: true);

        var result = UpdateValidator.Validate(request);

        Assert.True(result.IsValid);
    }
}
