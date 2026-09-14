namespace MediCore.Patient.Api.Authorization;

public static class PatientAuthorizationPolicies
{
    public const string FrontDesk = "FrontDesk";
    public const string PatientReader = "PatientReader";
    public const string ClinicalRecordWriter = "ClinicalRecordWriter";

    public static IServiceCollection AddPatientAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(FrontDesk, policy => policy.RequireRole("Admin", "Receptionist"))
            .AddPolicy(
                PatientReader,
                policy => policy.RequireRole("Admin", "Receptionist", "Doctor", "Nurse"))
            .AddPolicy(
                ClinicalRecordWriter,
                policy => policy.RequireRole("Doctor", "Nurse"));

        return services;
    }
}
