using FluentValidation;
using MediCore.Patient.Application.DTOs;

namespace MediCore.Patient.Application.Validators;

public sealed class ConditionRequestValidator : AbstractValidator<ConditionRequest>
{
    private static readonly string[] AllowedStatuses = ["Active", "Resolved", "Historical"];

    public ConditionRequestValidator()
    {
        RuleFor(condition => condition.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(condition => condition.Code)
            .MaximumLength(50)
            .When(condition => condition.Code is not null);

        RuleFor(condition => condition.ClinicalStatus)
            .NotEmpty()
            .Must(status => AllowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Clinical status must be Active, Resolved or Historical.");

        RuleFor(condition => condition.Notes)
            .MaximumLength(2000)
            .When(condition => condition.Notes is not null);
    }
}

public sealed class CreateMedicalRecordRequestValidator : AbstractValidator<CreateMedicalRecordRequest>
{
    public CreateMedicalRecordRequestValidator()
    {
        RuleFor(request => request.VisitReference).NotEmpty();
        AddContentRules();
    }

    private void AddContentRules()
    {
        RuleFor(request => request.ClinicalNotes)
            .NotEmpty()
            .MaximumLength(8000);

        RuleFor(request => request.Conditions)
            .NotNull()
            .Must(conditions => conditions is null || conditions.Count <= 20)
            .WithMessage("A medical record cannot contain more than 20 conditions.")
            .Must(HaveUniqueConditions)
            .WithMessage("A medical record cannot contain duplicate condition names.");

        RuleForEach(request => request.Conditions)
            .SetValidator(new ConditionRequestValidator());
    }

    private static bool HaveUniqueConditions(IReadOnlyList<ConditionRequest>? conditions) =>
        conditions is null || conditions
            .Select(condition => condition.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == conditions.Count;
}

public sealed class UpdateMedicalRecordRequestValidator : AbstractValidator<UpdateMedicalRecordRequest>
{
    public UpdateMedicalRecordRequestValidator()
    {
        RuleFor(request => request.ExpectedVersion).GreaterThanOrEqualTo(1);

        RuleFor(request => request.ClinicalNotes)
            .NotEmpty()
            .MaximumLength(8000);

        RuleFor(request => request.Conditions)
            .NotNull()
            .Must(conditions => conditions is null || conditions.Count <= 20)
            .WithMessage("A medical record cannot contain more than 20 conditions.")
            .Must(HaveUniqueConditions)
            .WithMessage("A medical record cannot contain duplicate condition names.");

        RuleForEach(request => request.Conditions)
            .SetValidator(new ConditionRequestValidator());
    }

    private static bool HaveUniqueConditions(IReadOnlyList<ConditionRequest>? conditions) =>
        conditions is null || conditions
            .Select(condition => condition.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == conditions.Count;
}

public sealed class MedicalRecordListRequestValidator : AbstractValidator<MedicalRecordListRequest>
{
    public MedicalRecordListRequestValidator()
    {
        RuleFor(request => request.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
    }
}
