using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Validators;

namespace MediCore.Patient.Tests.Unit;

public sealed class PatientSearchRequestValidatorTests
{
    private readonly PatientSearchRequestValidator _validator = new();

    [Fact]
    public void Request_uses_safe_default_pagination_values()
    {
        var request = new PatientSearchRequest();

        Assert.Null(request.Q);
        Assert.Equal(1, request.Page);
        Assert.Equal(20, request.PageSize);
        Assert.True(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 20)]
    [InlineData(10, 100)]
    public void Valid_pagination_is_accepted(int page, int pageSize)
    {
        var result = _validator.Validate(new PatientSearchRequest("Perera", page, pageSize));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1000001, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Invalid_pagination_is_rejected(int page, int pageSize)
    {
        var result = _validator.Validate(new PatientSearchRequest("Perera", page, pageSize));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Search_term_longer_than_100_characters_is_rejected()
    {
        var result = _validator.Validate(new PatientSearchRequest(new string('a', 101), 1, 20));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(PatientSearchRequest.Q));
    }

    [Fact]
    public void Blank_search_term_is_allowed_for_an_empty_result()
    {
        var result = _validator.Validate(new PatientSearchRequest("   ", 1, 20));

        Assert.True(result.IsValid);
    }
}
