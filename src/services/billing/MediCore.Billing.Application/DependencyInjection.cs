using FluentValidation;
using MediCore.Billing.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Billing.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IAppointmentBillingHandler, AppointmentBillingHandler>();
        services.AddScoped<IInvoiceQueryService, InvoiceQueryService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IServiceTariffService, ServiceTariffService>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
