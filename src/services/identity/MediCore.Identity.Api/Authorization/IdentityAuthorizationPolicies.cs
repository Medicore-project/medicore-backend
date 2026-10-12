namespace MediCore.Identity.Api.Authorization;

public static class IdentityAuthorizationPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string ClinicalStaff = "ClinicalStaff";
    public const string FrontDesk = "FrontDesk";

    /// <summary>
    /// Every clinic role. The patient service mints role-less booking tokens with the same key,
    /// issuer and audience as staff tokens, so a bare <c>[Authorize]</c> would let one through;
    /// naming the staff roles is what keeps the staff directory away from them.
    /// </summary>
    public const string StaffReader = "StaffReader";

    public static IServiceCollection AddIdentityAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminOnly, p => p.RequireRole("Admin"))
            .AddPolicy(ClinicalStaff, p => p.RequireRole("Admin", "Doctor", "Nurse"))
            .AddPolicy(FrontDesk, p => p.RequireRole("Admin", "Receptionist"))
            .AddPolicy(StaffReader, p => p.RequireRole("Admin", "Doctor", "Nurse", "Receptionist"));

        return services;
    }
}
