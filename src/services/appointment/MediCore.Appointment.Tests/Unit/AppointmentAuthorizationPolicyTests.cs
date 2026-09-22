using System.Reflection;
using System.Security.Claims;
using MediCore.Appointment.Api.Authorization;
using MediCore.Appointment.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Appointment.Tests.Unit;

public sealed class AppointmentAuthorizationPolicyTests
{
    // ── Who holds each policy ─────────────────────────────────────────────────

    [Fact]
    public async Task Leave_manager_policy_allows_only_doctors()
    {
        // Submitting and withdrawing leave is the doctor's own act. Admin and Receptionist were
        // removed deliberately so nobody can file or cancel leave on a doctor's behalf.
        var policy = await GetPolicyAsync(AppointmentAuthorizationPolicies.LeaveManager);

        var roleRequirement = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(["Doctor"], roleRequirement.AllowedRoles.OrderBy(role => role));
    }

    [Fact]
    public async Task Leave_reader_policy_allows_only_admin_and_doctors()
    {
        var policy = await GetPolicyAsync(AppointmentAuthorizationPolicies.LeaveReader);

        var roleRequirement = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(["Admin", "Doctor"], roleRequirement.AllowedRoles.OrderBy(role => role));
    }

    [Fact]
    public async Task Leave_approver_policy_allows_only_admin()
    {
        var policy = await GetPolicyAsync(AppointmentAuthorizationPolicies.LeaveApprover);

        var roleRequirement = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(["Admin"], roleRequirement.AllowedRoles.OrderBy(role => role));
    }

    [Fact]
    public async Task Schedule_reader_policy_still_covers_the_whole_clinic()
    {
        // Narrowing this would break the booking grid's "doctor on leave" labels for the front desk.
        var policy = await GetPolicyAsync(AppointmentAuthorizationPolicies.ScheduleReader);

        var roleRequirement = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal(
            ["Admin", "Doctor", "Nurse", "Receptionist"],
            roleRequirement.AllowedRoles.OrderBy(role => role));
    }

    [Theory]
    [InlineData("Doctor", true)]
    [InlineData("Admin", false)]
    [InlineData("Receptionist", false)]
    [InlineData("Nurse", false)]
    public async Task Leave_manager_policy_returns_expected_authorization_result(
        string role,
        bool expectedSuccess)
    {
        var result = await AuthorizeAsync(role, AppointmentAuthorizationPolicies.LeaveManager);

        Assert.Equal(expectedSuccess, result.Succeeded);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Doctor", true)]
    [InlineData("Receptionist", false)]
    [InlineData("Nurse", false)]
    public async Task Leave_reader_policy_returns_expected_authorization_result(
        string role,
        bool expectedSuccess)
    {
        var result = await AuthorizeAsync(role, AppointmentAuthorizationPolicies.LeaveReader);

        Assert.Equal(expectedSuccess, result.Succeeded);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Receptionist", true)]
    [InlineData("Doctor", true)]
    [InlineData("Nurse", true)]
    public async Task Approved_leave_dates_stay_readable_by_every_clinic_role(
        string role,
        bool expectedSuccess)
    {
        var result = await AuthorizeAsync(role, AppointmentAuthorizationPolicies.ScheduleReader);

        Assert.Equal(expectedSuccess, result.Succeeded);
    }

    // ── Which policy each endpoint carries ────────────────────────────────────

    [Theory]
    [InlineData(nameof(DoctorLeavesController.Create))]
    [InlineData(nameof(DoctorLeavesController.Withdraw))]
    public void Leave_write_endpoints_require_the_leave_manager_policy(string methodName)
    {
        var attribute = Assert.Single(GetAuthorizeAttributes<DoctorLeavesController>(methodName));

        Assert.Equal(AppointmentAuthorizationPolicies.LeaveManager, attribute.Policy);
    }

    [Fact]
    public void Full_leave_history_requires_the_leave_reader_policy()
    {
        var attribute = Assert.Single(
            GetAuthorizeAttributes<DoctorLeavesController>(nameof(DoctorLeavesController.GetForDoctor)));

        Assert.Equal(AppointmentAuthorizationPolicies.LeaveReader, attribute.Policy);
    }

    [Fact]
    public void Approved_leave_dates_require_only_the_schedule_reader_policy()
    {
        // The booking grid calls this one, so it must stay open to Receptionist and Nurse.
        var attribute = Assert.Single(
            GetAuthorizeAttributes<DoctorLeavesController>(nameof(DoctorLeavesController.GetApproved)));

        Assert.Equal(AppointmentAuthorizationPolicies.ScheduleReader, attribute.Policy);
    }

    [Theory]
    [InlineData(nameof(DoctorLeavesController.GetPending))]
    [InlineData(nameof(DoctorLeavesController.Review))]
    public void Approval_endpoints_require_the_leave_approver_policy(string methodName)
    {
        var attribute = Assert.Single(GetAuthorizeAttributes<DoctorLeavesController>(methodName));

        Assert.Equal(AppointmentAuthorizationPolicies.LeaveApprover, attribute.Policy);
    }

    [Theory]
    [InlineData(nameof(DoctorsController.List))]
    [InlineData(nameof(DoctorsController.GetById))]
    public void Doctor_directory_endpoints_require_the_schedule_reader_policy(string methodName)
    {
        // Same audience as the booking grid: every clinic role picks doctors when booking.
        var attribute = Assert.Single(GetAuthorizeAttributes<DoctorsController>(methodName));

        Assert.Equal(AppointmentAuthorizationPolicies.ScheduleReader, attribute.Policy);
    }

    [Fact]
    public void The_doctor_directory_is_not_anonymous()
    {
        // The gateway does not authenticate, so a missing [Authorize] would publish the staff list.
        Assert.NotNull(typeof(DoctorsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Empty(typeof(DoctorsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<AllowAnonymousAttribute>() is not null));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<AuthorizeAttribute> GetAuthorizeAttributes<TController>(string methodName) =>
        typeof(TController)
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttributes<AuthorizeAttribute>()
            .ToList();

    private static async Task<AuthorizationResult> AuthorizeAsync(string role, string policy)
    {
        await using var provider = BuildProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test");

        return await authorization.AuthorizeAsync(new ClaimsPrincipal(identity), resource: null, policy);
    }

    private static async Task<AuthorizationPolicy> GetPolicyAsync(string name)
    {
        await using var provider = BuildProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        return Assert.IsType<AuthorizationPolicy>(await policyProvider.GetPolicyAsync(name));
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppointmentAuthorization();

        return services.BuildServiceProvider();
    }
}
