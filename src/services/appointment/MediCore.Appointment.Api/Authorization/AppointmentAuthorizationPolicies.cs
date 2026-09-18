namespace MediCore.Appointment.Api.Authorization;

public static class AppointmentAuthorizationPolicies
{
    /// <summary>Create, change and delete doctor schedules, public holidays and slot blocks.</summary>
    public const string ScheduleManager = "ScheduleManager";

    /// <summary>Read schedules, slots, holidays and leave.</summary>
    public const string ScheduleReader = "ScheduleReader";

    /// <summary>
    /// Record and withdraw doctor leave. Doctors are included so they can manage their own leave;
    /// the "own leave only" ownership check needs a UserId to StaffId mapping and is deferred to
    /// SCRUM-33 (DoctorCache).
    /// </summary>
    public const string LeaveManager = "LeaveManager";

    public static IServiceCollection AddAppointmentAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(ScheduleManager, policy => policy.RequireRole("Admin", "Receptionist"))
            .AddPolicy(
                ScheduleReader,
                policy => policy.RequireRole("Admin", "Receptionist", "Doctor", "Nurse"))
            .AddPolicy(LeaveManager, policy => policy.RequireRole("Admin", "Receptionist", "Doctor"));

        return services;
    }
}
