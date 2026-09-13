using MediCore.Patient.Application.Entities;
using Microsoft.EntityFrameworkCore;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Infrastructure.Persistence;

public sealed class PatientDbContext : DbContext
{
    public PatientDbContext(DbContextOptions<PatientDbContext> options)
        : base(options)
    {
    }

    public DbSet<PatientEntity> Patients => Set<PatientEntity>();
    public DbSet<PatientAuditLog> PatientAuditLogs => Set<PatientAuditLog>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("medicore_patient");
        modelBuilder.HasSequence<long>("patient_number_seq", "medicore_patient")
            .StartsAt(1)
            .IncrementsBy(1);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PatientDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = utcNow;
                }

                if (string.IsNullOrWhiteSpace(entry.Entity.CreatedBy))
                {
                    entry.Entity.CreatedBy = "system";
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = utcNow;
                entry.Entity.UpdatedBy ??= "system";
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
