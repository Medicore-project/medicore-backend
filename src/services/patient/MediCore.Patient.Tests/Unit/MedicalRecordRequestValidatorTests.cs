using MediCore.Patient.Application.DTOs;
using MediCore.Patient.Application.Validators;

namespace MediCore.Patient.Tests.Unit;

public sealed class MedicalRecordRequestValidatorTests
{
    [Fact]
    public void Valid_create_request_passes_validation()
    {
        var result = new CreateMedicalRecordRequestValidator().Validate(CreateRequest());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_requires_visit_reference_and_clinical_notes()
    {
        var request = CreateRequest() with { VisitReference = Guid.Empty, ClinicalNotes = " " };

        var result = new CreateMedicalRecordRequestValidator().Validate(request);

        Assert.Contains(result.Errors, error => error.PropertyName == "VisitReference");
        Assert.Contains(result.Errors, error => error.PropertyName == "ClinicalNotes");
    }

    [Fact]
    public void Duplicate_condition_names_are_rejected_case_insensitively()
    {
        var request = CreateRequest() with
        {
            Conditions =
            [
                new("Hypertension", "I10", "Active", null),
                new(" hypertension ", null, "Historical", null)
            ]
        };

        var result = new CreateMedicalRecordRequestValidator().Validate(request);

        Assert.Contains(result.Errors, error => error.PropertyName == "Conditions");
    }

    [Fact]
    public void Invalid_condition_status_is_rejected()
    {
        var request = CreateRequest() with
        {
            Conditions = [new("Hypertension", "I10", "Pending", null)]
        };

        var result = new CreateMedicalRecordRequestValidator().Validate(request);

        Assert.Contains(result.Errors, error => error.PropertyName == "Conditions[0].ClinicalStatus");
    }

    [Fact]
    public void More_than_twenty_conditions_are_rejected()
    {
        var request = CreateRequest() with
        {
            Conditions = Enumerable.Range(1, 21)
                .Select(index => new ConditionRequest($"Condition {index}", null, "Active", null))
                .ToList()
        };

        var result = new CreateMedicalRecordRequestValidator().Validate(request);

        Assert.Contains(result.Errors, error => error.PropertyName == "Conditions");
    }

    [Fact]
    public void Update_requires_a_positive_expected_version()
    {
        var request = new UpdateMedicalRecordRequest(0, "Updated notes", []);

        var result = new UpdateMedicalRecordRequestValidator().Validate(request);

        Assert.Contains(result.Errors, error => error.PropertyName == "ExpectedVersion");
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Invalid_pagination_is_rejected(int page, int pageSize)
    {
        var result = new MedicalRecordListRequestValidator().Validate(new MedicalRecordListRequest
        {
            Page = page,
            PageSize = pageSize
        });

        Assert.False(result.IsValid);
    }

    private static CreateMedicalRecordRequest CreateRequest() => new(
        Guid.NewGuid(),
        "Patient reports persistent headache.",
        [new("Migraine", "G43", "Active", "Monitor symptoms")]);
}
