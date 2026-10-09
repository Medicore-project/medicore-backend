using Confluent.Kafka;
using MediCore.Billing.Application.Interfaces;
using MediCore.Billing.Application.Services;
using MediCore.Billing.Infrastructure.Email;
using MediCore.Billing.Infrastructure.Messaging;
using MediCore.Billing.Infrastructure.Persistence;
using MediCore.Billing.Infrastructure.Persistence.Repositories;
using MediCore.Billing.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
        services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();
        services.AddScoped<INotificationLogRepository, NotificationLogRepository>();
        services.AddScoped<IPatientContactRepository, PatientContactRepository>();
        services.AddScoped<IUnitOfWork, BillingUnitOfWork>();
        services.AddScoped<IRevenueReportQuery, RevenueReportQuery>();
        services.AddScoped<RevenueCsvExporter>();
        services.AddScoped<RevenuePdfExporter>();
        services.AddScoped<IOutstandingReportQuery, OutstandingReportQuery>();
        services.AddScoped<OutstandingCsvExporter>();
        services.AddScoped<OutstandingPdfExporter>();

        var smtpOptions = new SmtpOptions
        {
            Host = configuration["Smtp:Host"] ?? "localhost",
            Port = int.TryParse(configuration["Smtp:Port"], out var smtpPort) ? smtpPort : 1025,
            EnableSsl = bool.TryParse(configuration["Smtp:EnableSsl"], out var enableSsl) && enableSsl,
            SendTimeout = TimeSpan.FromSeconds(
                int.TryParse(configuration["Smtp:SendTimeoutSeconds"], out var sendTimeoutSeconds)
                    ? Math.Clamp(sendTimeoutSeconds, 1, 120)
                    : 10),
            FromAddress = configuration["Smtp:FromAddress"] ?? "noreply@medicore.local",
            FromName = configuration["Smtp:FromName"] ?? "MediCore",
            Username = configuration["Smtp:Username"],
            Password = configuration["Smtp:Password"]
        };
        services.AddSingleton(smtpOptions);
        services.AddScoped<IEmailSender, SmtpEmailSender>();

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

        var notificationTopics = configuration
            .GetSection("Kafka:NotificationConsumer:Topics")
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        var notificationRetryDelay = int.TryParse(
            configuration["Kafka:NotificationConsumer:RetryDelaySeconds"], out var configuredNotificationDelay)
            ? Math.Max(0, configuredNotificationDelay)
            : 2;
        var maxDeliveryAttempts = int.TryParse(
            configuration["Kafka:NotificationConsumer:MaxDeliveryAttempts"], out var configuredMaxAttempts)
            ? Math.Clamp(configuredMaxAttempts, 1, 10)
            : 3;
        var notificationOptions = new NotificationConsumerOptions
        {
            BootstrapServers = bootstrapServers,
            SaslUsername = options.SaslUsername,
            SaslPassword = options.SaslPassword,
            GroupId = configuration["Kafka:NotificationConsumer:GroupId"] ?? "medicore-billing-notifications",
            Topics = notificationTopics.Length == 0 ? NotificationConsumerOptions.DefaultTopics : notificationTopics,
            RetryDelay = TimeSpan.FromSeconds(notificationRetryDelay),
            MaxDeliveryAttempts = maxDeliveryAttempts
        };
        services.AddSingleton(notificationOptions);
        services.AddSingleton<INotificationKafkaConsumerFactory, NotificationKafkaConsumerFactory>();
        services.AddScoped<INotificationEventProcessor, NotificationEventProcessor>();

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All
        };
        if (!string.IsNullOrEmpty(options.SaslUsername) && !string.IsNullOrEmpty(options.SaslPassword))
        {
            producerConfig.SecurityProtocol = SecurityProtocol.SaslSsl;
            producerConfig.SaslMechanism = SaslMechanism.Plain;
            producerConfig.SaslUsername = options.SaslUsername;
            producerConfig.SaslPassword = options.SaslPassword;
        }

        services.AddSingleton<IProducer<string, string>>(_ =>
            new ProducerBuilder<string, string>(producerConfig).Build());
        services.AddSingleton<IKafkaEventPublisher, KafkaEventPublisher>();
        services.AddSingleton<INotificationFailurePublisher, NotificationFailurePublisher>();
        services.AddHostedService<OutboxProcessor>();
        services.AddSingleton<IHostedService>(provider => CreateNotificationConsumer(provider, retryOnly: false));
        services.AddSingleton<IHostedService>(provider => CreateNotificationConsumer(provider, retryOnly: true));
        return services;
    }

    private static NotificationEventsConsumer CreateNotificationConsumer(
        IServiceProvider provider,
        bool retryOnly) => new(
            provider.GetRequiredService<INotificationKafkaConsumerFactory>(),
            provider.GetRequiredService<INotificationFailurePublisher>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<NotificationConsumerOptions>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NotificationEventsConsumer>>(),
            retryOnly);
}
