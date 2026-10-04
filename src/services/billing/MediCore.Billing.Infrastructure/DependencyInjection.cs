using MediCore.Billing.Application.Interfaces;
using MediCore.Billing.Infrastructure.Messaging;
using MediCore.Billing.Infrastructure.Persistence;
using MediCore.Billing.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Billing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BillingDatabase")
            ?? throw new InvalidOperationException("Connection string 'BillingDatabase' is missing.");

        services.AddDbContext<BillingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                Microsoft.EntityFrameworkCore.Migrations.HistoryRepository.DefaultTableName,
                "medicore_billing")));
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IServiceTariffRepository, ServiceTariffRepository>();
        services.AddScoped<IProcessedMessageRepository, ProcessedMessageRepository>();
        services.AddScoped<IUnitOfWork, BillingUnitOfWork>();

        var bootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka setting 'Kafka:BootstrapServers' is missing.");
        var retryDelaySeconds = int.TryParse(
            configuration["Kafka:AppointmentConsumer:RetryDelaySeconds"], out var retryDelay)
            ? Math.Max(0, retryDelay)
            : 2;
        var options = new AppointmentConsumerOptions
        {
            BootstrapServers = bootstrapServers,
            SaslUsername = configuration["Kafka:SaslUsername"],
            SaslPassword = configuration["Kafka:SaslPassword"],
            Topic = configuration["Kafka:AppointmentConsumer:Topic"] ?? AppointmentConsumerOptions.DefaultTopic,
            GroupId = configuration["Kafka:AppointmentConsumer:GroupId"] ?? AppointmentConsumerOptions.DefaultGroupId,
            RetryDelay = TimeSpan.FromSeconds(retryDelaySeconds)
        };

        services.AddSingleton(options);
        services.AddSingleton<IAppointmentKafkaConsumerFactory, AppointmentKafkaConsumerFactory>();
        services.AddScoped<IAppointmentEventProcessor, AppointmentEventProcessor>();
        services.AddHostedService<AppointmentEventsConsumer>();
        return services;
    }
}
