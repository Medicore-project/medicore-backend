using MediCore.Identity.Application.Interfaces;
using MediCore.Identity.Infrastructure.Persistence;
using MediCore.Identity.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Confluent.Kafka;
using MediCore.Identity.Infrastructure.Messaging;

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
            options.UseNpgsql(connectionString));

        services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();

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

        return services;
    }
}