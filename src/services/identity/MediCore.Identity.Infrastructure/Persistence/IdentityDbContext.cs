using MediCore.Identity.Application.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<User> Users => Set<User>();
    public DbSet<StaffProfile> StaffProfiles => Set<StaffProfile>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Specialization> Specializations => Set<Specialization>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("medicore_identity");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<IAuditableEntity>();
        var utcNow = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = utcNow;
                entry.Entity.CreatedBy = "system";
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = utcNow;
                entry.Entity.UpdatedBy = "system";
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}