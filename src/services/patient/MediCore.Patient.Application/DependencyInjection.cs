using FluentValidation;
using MediCore.Patient.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Patient.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IPatientRegistrationService, PatientRegistrationService>();
        services.AddScoped<IPatientProfileService, PatientProfileService>();
        services.AddScoped<IPatientSearchService, PatientSearchService>();
        services.AddScoped<IMedicalRecordService, MedicalRecordService>();
        services.AddScoped<IPrescriptionService, PrescriptionService>();
        services.AddScoped<IAllergyService, AllergyService>();
        services.AddScoped<IAppointmentCompletedHandler, AppointmentCompletedHandler>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
