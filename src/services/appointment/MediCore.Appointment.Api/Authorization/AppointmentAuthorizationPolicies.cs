namespace MediCore.Appointment.Api.Authorization;

public static class AppointmentAuthorizationPolicies
{
    /// <summary>Create, change and delete doctor schedules and slot blocks.</summary>
    public const string ScheduleManager = "ScheduleManager";

    /// <summary>Read schedules, slots, holidays and leave.</summary>
    public const string ScheduleReader = "ScheduleReader";

    /// <summary>
    /// Declare and withdraw clinic-wide public holidays.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="ScheduleManager"/> on purpose: a holiday suppresses slots for
    /// every doctor in the clinic on that date, which is an operational decision rather than a
    /// front-desk one, so it is restricted to Admin.
    /// </remarks>
    public const string HolidayManager = "HolidayManager";

    /// <summary>
    /// Submit and withdraw doctor leave requests.
    /// </summary>
    /// <remarks>
    /// Doctor-only, and not merely by role: <see cref="Api.Controllers.DoctorLeavesController"/>
    /// additionally checks the caller's <c>staffId</c> claim against the request's
    /// <c>DoctorId</c>, so a doctor can submit or withdraw only their own leave — Admin and
    /// Receptionist cannot act on a doctor's behalf, and one doctor cannot touch another's request.
    /// Submitting does not grant leave. A request created under this policy is
    /// <c>Pending</c> and has no effect on slots until someone holding
    /// <see cref="LeaveApprover"/> approves it.
    /// </remarks>
    public const string LeaveManager = "LeaveManager";

    /// <summary>
    /// Approve or reject a pending leave request.
    /// </summary>
    /// <remarks>
    /// Deliberately excludes Doctor, so a doctor can never approve their own request, and excludes
    /// Receptionist, since granting leave is not a front-desk decision. Admin is the seeded role
    /// carrying "Full system access" and is therefore the super-admin this workflow requires — the
    /// Identity service seeds no separate SuperAdmin role.
    /// </remarks>
    public const string LeaveApprover = "LeaveApprover";

    public static IServiceCollection AddAppointmentAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(ScheduleManager, policy => policy.RequireRole("Admin", "Receptionist"))
            .AddPolicy(
                ScheduleReader,
                policy => policy.RequireRole("Admin", "Receptionist", "Doctor", "Nurse"))
            .AddPolicy(HolidayManager, policy => policy.RequireRole("Admin"))
            .AddPolicy(LeaveManager, policy => policy.RequireRole("Doctor"))
            .AddPolicy(LeaveApprover, policy => policy.RequireRole("Admin"));

        return services;
    }
}
