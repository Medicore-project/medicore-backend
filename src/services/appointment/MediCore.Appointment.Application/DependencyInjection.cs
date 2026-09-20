using FluentValidation;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Appointment.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.Configure<SchedulingOptions>(
            configuration.GetSection(SchedulingOptions.SectionName));

        services.AddScoped<ISlotGenerator, SlotGenerator>();
        services.AddScoped<ISlotReconciler, SlotReconciler>();
        services.AddScoped<IScheduleOverlapDetector, ScheduleOverlapDetector>();
        services.AddScoped<IScheduleRevisionService, ScheduleRevisionService>();
        services.AddScoped<IDoctorScheduleService, DoctorScheduleService>();
        services.AddScoped<ISlotService, SlotService>();
        services.AddScoped<IPublicHolidayService, PublicHolidayService>();
        services.AddScoped<IDoctorLeaveService, DoctorLeaveService>();

        // Injected into application services so tests can pin "now" without touching the clock.
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
