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

    // ── Booking (SCRUM-34) ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Receptionist", true)]
    [InlineData("Doctor", false)]
    [InlineData("Nurse", false)]
    // A logged-in patient identifies with their patient number and date of birth like anyone else
    // and books with the token that produces. Passing on the role alone would let them post any
    // patientId in the body and book for a stranger.
    [InlineData("Patient", false)]
    public async Task Booking_by_role_is_front_desk_only(string role, bool expected)
    {
        var result = await AuthorizeAsync(role, AppointmentAuthorizationPolicies.BookingCreator);

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task A_booking_token_satisfies_the_policy_with_no_role_at_all()
    {
        // This is the whole point of the assertion policy: the two branches are an OR. A booking
        // token deliberately carries no role claim, so it passes here and nowhere else.
        var result = await AuthorizeAsync(
            AppointmentAuthorizationPolicies.BookingCreator,
            new Claim(AppointmentAuthorizationPolicies.PatientIdClaim, Guid.NewGuid().ToString()));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_caller_with_neither_a_front_desk_role_nor_a_patient_claim_is_refused()
    {
        var result = await AuthorizeAsync(AppointmentAuthorizationPolicies.BookingCreator);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Booking_requires_the_booking_creator_policy()
    {
        var attribute = Assert.Single(
            GetAuthorizeAttributes<AppointmentsController>(nameof(AppointmentsController.Book)));

        Assert.Equal(AppointmentAuthorizationPolicies.BookingCreator, attribute.Policy);
    }

    [Fact]
    public void Reading_an_appointment_requires_the_schedule_reader_policy()
    {
        var attribute = Assert.Single(
            GetAuthorizeAttributes<AppointmentsController>(nameof(AppointmentsController.GetById)));

        Assert.Equal(AppointmentAuthorizationPolicies.ScheduleReader, attribute.Policy);
    }

    [Fact]
    public void The_clinic_appointment_list_requires_the_schedule_reader_policy()
    {
        // The same audience as the booking grid it feeds.
        var attribute = Assert.Single(
            GetAuthorizeAttributes<AppointmentsController>(nameof(AppointmentsController.List)));

        Assert.Equal(AppointmentAuthorizationPolicies.ScheduleReader, attribute.Policy);
    }

    [Fact]
    public void Reading_ones_own_bookings_requires_the_booking_holder_policy()
    {
        var attribute = Assert.Single(
            GetAuthorizeAttributes<AppointmentsController>(nameof(AppointmentsController.Mine)));

        Assert.Equal(AppointmentAuthorizationPolicies.BookingHolder, attribute.Policy);
    }

    [Fact]
    public async Task A_booking_token_holds_its_own_bookings()
    {
        var result = await AuthorizeAsync(
            AppointmentAuthorizationPolicies.BookingHolder,
            new Claim(AppointmentAuthorizationPolicies.PatientIdClaim, Guid.NewGuid().ToString()));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Receptionist")]
    [InlineData("Doctor")]
    [InlineData("Nurse")]
    // A logged-in Patient role is not a booking token either: "mine" is whoever the token names.
    [InlineData("Patient")]
    public async Task No_role_alone_holds_a_patients_bookings(string role)
    {
        var result = await AuthorizeAsync(role, AppointmentAuthorizationPolicies.BookingHolder);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Booking_is_not_anonymous()
    {
        // The gateway does not authenticate. An anonymous booking endpoint would let anyone take
        // any slot for any patient id they cared to guess.
        Assert.NotNull(typeof(AppointmentsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Empty(typeof(AppointmentsController)
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

    /// <summary>The role-less variant, for principals identified by a claim instead.</summary>
    private static async Task<AuthorizationResult> AuthorizeAsync(string policy, params Claim[] claims)
    {
        await using var provider = BuildProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var identity = new ClaimsIdentity(claims, "Test");

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
