using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MediCore.Appointment.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c> / <c>dotnet ef database update</c>.
/// The fallback connection string targets the local docker-compose PostgreSQL instance.
/// </summary>
public sealed class AppointmentDbContextFactory : IDesignTimeDbContextFactory<AppointmentDbContext>
{
    public AppointmentDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AppointmentDatabase")
            ?? "Host=localhost;Port=5432;Database=medicore;Username=appointment_svc;Password=appointment_pass;SearchPath=medicore_appointment";
        var options = new DbContextOptionsBuilder<AppointmentDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                Microsoft.EntityFrameworkCore.Migrations.HistoryRepository.DefaultTableName,
                AppointmentDbContext.SchemaName))
            .Options;

        return new AppointmentDbContext(options);
    }
}
