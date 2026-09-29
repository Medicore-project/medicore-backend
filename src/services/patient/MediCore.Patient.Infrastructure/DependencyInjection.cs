using Confluent.Kafka;
using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Infrastructure.Auth;
using MediCore.Patient.Infrastructure.Messaging;
using MediCore.Patient.Infrastructure.Persistence;
using MediCore.Patient.Infrastructure.Persistence.Repositories;
using MediCore.Patient.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;

namespace MediCore.Patient.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PatientDatabase")
            ?? throw new InvalidOperationException("Connection string 'PatientDatabase' is missing.");

        services.AddDbContext<PatientDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                Microsoft.EntityFrameworkCore.Migrations.HistoryRepository.DefaultTableName,
                "medicore_patient")));

        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IPatientSearchRepository, PatientRepository>();
        services.AddScoped<IMedicalRecordRepository, MedicalRecordRepository>();
        services.AddScoped<IPrescriptionRepository, PrescriptionRepository>();
        services.AddScoped<IAllergyRepository, AllergyRepository>();
        services.AddScoped<IPatientAuditRepository, PatientAuditRepository>();
        services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
        services.AddScoped<IProcessedMessageRepository, ProcessedMessageRepository>();
        services.AddScoped<IDemographicsReportQuery, DemographicsReportQuery>();
        services.AddSingleton<IDemographicsCsvExporter, DemographicsCsvExporter>();
        services.AddSingleton<IDemographicsPdfExporter, DemographicsPdfExporter>();
        services.AddScoped<IUnitOfWork, PatientUnitOfWork>();
        services.AddSingleton<IBookingTokenGenerator, BookingTokenGenerator>();

        QuestPDF.Settings.License = LicenseType.Community;

        var kafkaBootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka setting 'Kafka:BootstrapServers' is missing.");
        var retryDelaySeconds = int.TryParse(
            configuration["Kafka:AppointmentConsumer:RetryDelaySeconds"],
            out var configuredRetryDelaySeconds)
            ? Math.Max(0, configuredRetryDelaySeconds)
            : 2;

        var appointmentConsumerOptions = new AppointmentConsumerOptions
        {
            BootstrapServers = kafkaBootstrapServers,
            Topic = configuration["Kafka:AppointmentConsumer:Topic"]
                ?? AppointmentConsumerOptions.DefaultTopic,
            GroupId = configuration["Kafka:AppointmentConsumer:GroupId"]
                ?? AppointmentConsumerOptions.DefaultGroupId,
            RetryDelay = TimeSpan.FromSeconds(retryDelaySeconds)
        };

        services.AddSingleton<IProducer<string, string>>(_ =>
            new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = kafkaBootstrapServers,
                EnableIdempotence = true,
                Acks = Acks.All
            }).Build());

        services.AddSingleton<IKafkaEventPublisher, KafkaEventPublisher>();
        services.AddSingleton(appointmentConsumerOptions);
        services.AddSingleton<IAppointmentKafkaConsumerFactory, AppointmentKafkaConsumerFactory>();
        services.AddScoped<IAppointmentEventProcessor, AppointmentEventProcessor>();
        services.AddHostedService<OutboxProcessor>();
        services.AddHostedService<AppointmentCompletedConsumer>();

        return services;
    }
}
