using MediCore.Billing.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Billing.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAppointmentBillingHandler, AppointmentBillingHandler>();
        services.AddScoped<IInvoiceQueryService, InvoiceQueryService>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
