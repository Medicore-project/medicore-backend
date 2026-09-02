using Confluent.Kafka;
using MediCore.Identity.Application.DTOs;
using MediCore.Identity.Application.Interfaces;
using MediCore.Identity.Infrastructure.Auth;
using MediCore.Identity.Infrastructure.Messaging;
using MediCore.Identity.Infrastructure.Persistence;
using MediCore.Identity.Infrastructure.Persistence.Repositories;
using MediCore.Identity.Infrastructure.Reporting;
using MediCore.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("IdentityDatabase")
            ?? throw new InvalidOperationException(
                "Connection string 'IdentityDatabase' is missing.");

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, x => x.MigrationsHistoryTable(
                Microsoft.EntityFrameworkCore.Migrations.HistoryRepository.DefaultTableName, 
                "medicore_identity")));

        services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<ISpecializationRepository, SpecializationRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddTransient<IJwtTokenGenerator, JwtTokenGenerator>();

        var kafkaBootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException(
                "Kafka setting 'Kafka:BootstrapServers' is missing.");

        services.AddSingleton<IProducer<string, string>>(_ =>
            new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = kafkaBootstrapServers,
                EnableIdempotence = true,
                Acks = Acks.All
            }).Build());

        services.AddSingleton<IKafkaEventPublisher, KafkaEventPublisher>();
        services.AddHostedService<OutboxProcessor>();

        services.AddScoped<IReportQuery<AuditReportFilter, AuditReportRow>, AuditReportQuery>();

        return services;
    }
}