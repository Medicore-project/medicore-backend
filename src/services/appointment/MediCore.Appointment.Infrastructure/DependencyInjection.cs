using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Infrastructure.Messaging;
using MediCore.Appointment.Infrastructure.Persistence;
using MediCore.Appointment.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Appointment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AppointmentDatabase")
            ?? throw new InvalidOperationException("Connection string 'AppointmentDatabase' is missing.");

        services.AddDbContext<AppointmentDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                Microsoft.EntityFrameworkCore.Migrations.HistoryRepository.DefaultTableName,
                AppointmentDbContext.SchemaName)));

        services.AddScoped<IDoctorScheduleRepository, DoctorScheduleRepository>();
        services.AddScoped<ISlotRepository, SlotRepository>();
        services.AddScoped<IPublicHolidayRepository, PublicHolidayRepository>();
        services.AddScoped<IDoctorLeaveRepository, DoctorLeaveRepository>();
        services.AddScoped<IDoctorCacheRepository, DoctorCacheRepository>();
        services.AddScoped<IProcessedMessageRepository, ProcessedMessageRepository>();
        services.AddScoped<IUnitOfWork, AppointmentUnitOfWork>();

        AddStaffEventsConsumer(services, configuration);

        // The Kafka outbox processor is wired up when event publishing lands (SCRUM-34).

        return services;
    }

    /// <summary>
    /// Feeds the doctor cache from staff-events. Skipped when <c>Kafka:BootstrapServers</c> is not
    /// set, so a host without Kafka — a test host, say — still starts; the cache then simply stays
    /// as it is.
    /// </summary>
    private static void AddStaffEventsConsumer(IServiceCollection services, IConfiguration configuration)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            return;
        }

        var retryDelaySeconds = int.TryParse(
            configuration["Kafka:StaffConsumer:RetryDelaySeconds"],
            out var configuredRetryDelaySeconds)
            ? Math.Max(0, configuredRetryDelaySeconds)
            : 2;

        services.AddSingleton(new StaffConsumerOptions
        {
            BootstrapServers = bootstrapServers,
            Topic = configuration["Kafka:StaffConsumer:Topic"] ?? StaffConsumerOptions.DefaultTopic,
            GroupId = configuration["Kafka:StaffConsumer:GroupId"] ?? StaffConsumerOptions.DefaultGroupId,
            RetryDelay = TimeSpan.FromSeconds(retryDelaySeconds)
        });
        services.AddSingleton<IStaffKafkaConsumerFactory, StaffKafkaConsumerFactory>();
        services.AddScoped<IStaffEventProcessor, StaffEventProcessor>();
        services.AddHostedService<StaffEventsConsumer>();
    }
}
