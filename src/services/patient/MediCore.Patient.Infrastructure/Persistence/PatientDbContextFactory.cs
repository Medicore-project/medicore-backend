using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MediCore.Patient.Infrastructure.Persistence;

public sealed class PatientDbContextFactory : IDesignTimeDbContextFactory<PatientDbContext>
{
    public PatientDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PatientDatabase")
            ?? "Host=localhost;Port=5432;Database=medicore;Username=patient_svc;Password=patient_pass;SearchPath=medicore_patient";
        var options = new DbContextOptionsBuilder<PatientDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(
                Microsoft.EntityFrameworkCore.Migrations.HistoryRepository.DefaultTableName,
                "medicore_patient"))
            .Options;

        return new PatientDbContext(options);
    }
}
