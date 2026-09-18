using MediCore.Appointment.Infrastructure.Persistence;
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

        // Repositories, the unit of work and the Kafka outbox are registered in
        // SCRUM-32 Steps 1 and 5 as the corresponding types are introduced.

        return services;
    }
}
