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
        services.AddScoped<INotificationTemplateService, NotificationTemplateService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRevenueReportService, RevenueReportService>();
        services.AddScoped<IOutstandingReportService, OutstandingReportService>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
