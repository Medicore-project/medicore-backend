using Confluent.Kafka;
using MediCore.Patient.Application.Interfaces;
using MediCore.Patient.Infrastructure.Messaging;
using MediCore.Patient.Infrastructure.Persistence;
using MediCore.Patient.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddScoped<IPatientAuditRepository, PatientAuditRepository>();
        services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
        services.AddScoped<IUnitOfWork, PatientUnitOfWork>();

        var kafkaBootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka setting 'Kafka:BootstrapServers' is missing.");

        services.AddSingleton<IProducer<string, string>>(_ =>
            new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = kafkaBootstrapServers,
                EnableIdempotence = true,
                Acks = Acks.All
            }).Build());

        services.AddSingleton<IKafkaEventPublisher, KafkaEventPublisher>();
        services.AddHostedService<OutboxProcessor>();

        return services;
    }
}
