using Confluent.Kafka;
using MediCore.Appointment.Application.Interfaces;
using MediCore.Appointment.Application.Scheduling;
using MediCore.Appointment.Infrastructure.Messaging;
using MediCore.Appointment.Infrastructure.Persistence;
using MediCore.Appointment.Infrastructure.Persistence.Repositories;
using MediCore.Appointment.Infrastructure.Reporting;
using MediCore.Appointment.Infrastructure.Waitlist;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;

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
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IAppointmentHistoryRepository, AppointmentHistoryRepository>();
        services.AddScoped<IProcessedMessageRepository, ProcessedMessageRepository>();
        services.AddScoped<IWaitlistRepository, WaitlistRepository>();
        // Registered unconditionally: booking writes outbox rows whether or not a broker exists.
        services.AddScoped<IOutboxMessageRepository, OutboxMessageRepository>();
        services.AddScoped<IUnitOfWork, AppointmentUnitOfWork>();

        // SCRUM-38 utilisation report: raw ADO.NET on the scoped context, stateless exporters.
        services.AddScoped<IUtilisationReportQuery, UtilisationReportQuery>();
        services.AddSingleton<IUtilisationCsvExporter, UtilisationCsvExporter>();
        services.AddSingleton<IUtilisationPdfExporter, UtilisationPdfExporter>();
        QuestPDF.Settings.License = LicenseType.Community;

        AddStaffEventsConsumer(services, configuration);
        AddOutboxPublisher(services, configuration);
        AddWaitlistSweeper(services, configuration);

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

    /// <summary>
    /// Drains the outbox to Kafka. Skipped when <c>Kafka:BootstrapServers</c> is not set, exactly
    /// as <see cref="AddStaffEventsConsumer"/> is — a host without Kafka must still start and
    /// bookings must still save, with their event rows simply waiting for a host that has a broker.
    /// Deliberately not the Patient service's throw-if-missing: the integration test host blanks
    /// this setting on purpose.
    /// </summary>
    private static void AddOutboxPublisher(IServiceCollection services, IConfiguration configuration)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            return;
        }

        services.AddSingleton<IProducer<string, string>>(_ =>
            new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                EnableIdempotence = true,
                Acks = Acks.All
            }).Build());
        services.AddSingleton<IKafkaEventPublisher, KafkaEventPublisher>();
        services.AddHostedService<OutboxProcessor>();
    }

    /// <summary>
    /// Expires lapsed waitlist offers and passes them on (SCRUM-37). Registered with or without
    /// Kafka — it needs only the database — and skipped when
    /// <c>Appointments:Waitlist:SweepIntervalSeconds</c> is zero or less, which the integration test
    /// host sets so a background pass never changes rows under a test. Unset means the default.
    /// </summary>
    internal static void AddWaitlistSweeper(IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration[$"{WaitlistOptions.SectionName}:{nameof(WaitlistOptions.SweepIntervalSeconds)}"];
        var seconds = int.TryParse(configured, out var parsed)
            ? parsed
            : new WaitlistOptions().SweepIntervalSeconds;

        if (seconds <= 0)
        {
            return;
        }

        services.AddSingleton(new WaitlistSweepSchedule(TimeSpan.FromSeconds(seconds)));
        services.AddHostedService<WaitlistSweepProcessor>();
    }
}
