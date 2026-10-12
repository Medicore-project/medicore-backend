using System.Reflection;
using System.Security.Claims;
using MediCore.Identity.Api.Authorization;
using MediCore.Identity.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Identity.Tests.Unit;

public sealed class IdentityAuthorizationPolicyTests
{
    [Fact]
    public async Task Staff_reader_policy_allows_every_clinic_role()
    {
        var policy = await GetPolicyAsync(IdentityAuthorizationPolicies.StaffReader);

        var roleRequirement = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(
            ["Admin", "Doctor", "Nurse", "Receptionist"],
            roleRequirement.AllowedRoles.OrderBy(role => role));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Doctor", true)]
    [InlineData("Nurse", true)]
    [InlineData("Receptionist", true)]
    [InlineData("Patient", false)]
    public async Task Staff_reader_policy_returns_expected_authorization_result(string role, bool expectedSuccess)
    {
        var result = await AuthorizeAsync(
            new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"),
            IdentityAuthorizationPolicies.StaffReader);

        Assert.Equal(expectedSuccess, result.Succeeded);
    }

    [Fact]
    public async Task A_booking_token_cannot_read_the_staff_directory()
    {
        // The shape the patient service mints for self-booking: authenticated, no role at all.
        var bookingToken = new ClaimsIdentity(
            [
                new Claim("patientId", Guid.NewGuid().ToString()),
                new Claim("token_use", "booking"),
            ],
            "Test");

        var result = await AuthorizeAsync(bookingToken, IdentityAuthorizationPolicies.StaffReader);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void The_staff_controller_requires_the_staff_reader_policy()
    {
        var attribute = Assert.Single(typeof(StaffController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(IdentityAuthorizationPolicies.StaffReader, attribute.Policy);
    }

    [Theory]
    [InlineData(nameof(StaffController.GetAll))]
    [InlineData(nameof(StaffController.GetById))]
    public void Staff_read_endpoints_add_no_policy_of_their_own(string methodName)
    {
        Assert.Empty(GetAuthorizeAttributes<StaffController>(methodName));
    }

    [Theory]
    [InlineData(nameof(StaffController.Create))]
    [InlineData(nameof(StaffController.Update))]
    [InlineData(nameof(StaffController.Delete))]
    [InlineData(nameof(StaffController.RepublishDoctors))]
    [InlineData(nameof(StaffController.AssignRole))]
    public void Staff_write_endpoints_remain_admin_only(string methodName)
    {
        var attribute = Assert.Single(GetAuthorizeAttributes<StaffController>(methodName));

        Assert.Equal(IdentityAuthorizationPolicies.AdminOnly, attribute.Policy);
    }

    private static IReadOnlyList<AuthorizeAttribute> GetAuthorizeAttributes<TController>(string methodName) =>
        typeof(TController)
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttributes<AuthorizeAttribute>()
            .ToList();

    private static async Task<AuthorizationResult> AuthorizeAsync(ClaimsIdentity identity, string policy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityAuthorization();
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        return await authorization.AuthorizeAsync(new ClaimsPrincipal(identity), resource: null, policy);
    }

    private static async Task<AuthorizationPolicy> GetPolicyAsync(string name)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityAuthorization();
        await using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        return Assert.IsType<AuthorizationPolicy>(await policyProvider.GetPolicyAsync(name));
    }
}
