using System.Reflection;
using System.Security.Claims;
using MediCore.Patient.Api.Authorization;
using MediCore.Patient.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Patient.Tests.Unit;

public sealed class PatientAuthorizationPolicyTests
{
    [Fact]
    public async Task Clinical_record_writer_policy_allows_only_doctors_and_nurses()
    {
        var policy = await GetPolicyAsync(PatientAuthorizationPolicies.ClinicalRecordWriter);

        var roleRequirement = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(
            ["Doctor", "Nurse"],
            roleRequirement.AllowedRoles.OrderBy(role => role));
    }

    [Theory]
    [InlineData("Doctor", true)]
    [InlineData("Nurse", true)]
    [InlineData("Admin", false)]
    [InlineData("Receptionist", false)]
    [InlineData("Patient", false)]
    public async Task Clinical_record_writer_policy_returns_expected_authorization_result(
        string role,
        bool expectedSuccess)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPatientAuthorization();
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test");

        var result = await authorization.AuthorizeAsync(
            new ClaimsPrincipal(identity),
            resource: null,
            PatientAuthorizationPolicies.ClinicalRecordWriter);

        Assert.Equal(expectedSuccess, result.Succeeded);
    }

    [Fact]
    public async Task Patient_reader_policy_allows_front_desk_and_clinical_staff()
    {
        var policy = await GetPolicyAsync(PatientAuthorizationPolicies.PatientReader);

        var roleRequirement = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(
            ["Admin", "Doctor", "Nurse", "Receptionist"],
            roleRequirement.AllowedRoles.OrderBy(role => role));
    }

    [Theory]
    [InlineData(nameof(MedicalRecordsController.Create))]
    [InlineData(nameof(MedicalRecordsController.Update))]
    [InlineData(nameof(MedicalRecordsController.Delete))]
    public void Medical_record_write_endpoints_require_clinical_writer_policy(string methodName)
    {
        var attribute = Assert.Single(GetAuthorizeAttributes<MedicalRecordsController>(methodName));

        Assert.Equal(PatientAuthorizationPolicies.ClinicalRecordWriter, attribute.Policy);
    }

    [Theory]
    [InlineData(nameof(MedicalRecordsController.GetPage))]
    [InlineData(nameof(MedicalRecordsController.GetById))]
    [InlineData(nameof(MedicalRecordsController.GetVersions))]
    public void Medical_record_read_endpoints_do_not_require_writer_role(string methodName)
    {
        Assert.Empty(GetAuthorizeAttributes<MedicalRecordsController>(methodName));
        Assert.Single(typeof(MedicalRecordsController).GetCustomAttributes<AuthorizeAttribute>());
    }

    [Theory]
    [InlineData(nameof(PatientsController.Search))]
    [InlineData(nameof(PatientsController.GetById))]
    public void Patient_lookup_endpoints_allow_patient_readers(string methodName)
    {
        var attribute = Assert.Single(GetAuthorizeAttributes<PatientsController>(methodName));

        Assert.Equal(PatientAuthorizationPolicies.PatientReader, attribute.Policy);
    }

    [Theory]
    [InlineData(nameof(PatientsController.Register))]
    [InlineData(nameof(PatientsController.Update))]
    [InlineData(nameof(PatientsController.Delete))]
    public void Patient_mutation_endpoints_remain_front_desk_only(string methodName)
    {
        var attribute = Assert.Single(GetAuthorizeAttributes<PatientsController>(methodName));

        Assert.Equal(PatientAuthorizationPolicies.FrontDesk, attribute.Policy);
    }

    private static IReadOnlyList<AuthorizeAttribute> GetAuthorizeAttributes<TController>(string methodName) =>
        typeof(TController)
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttributes<AuthorizeAttribute>()
            .ToList();

    private static async Task<AuthorizationPolicy> GetPolicyAsync(string name)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPatientAuthorization();
        await using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        return Assert.IsType<AuthorizationPolicy>(await policyProvider.GetPolicyAsync(name));
    }
}
