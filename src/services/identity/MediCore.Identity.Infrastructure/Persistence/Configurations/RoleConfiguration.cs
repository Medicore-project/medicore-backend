using MediCore.Identity.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Identity.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public static readonly Guid AdminId    = new("a1a1a1a1-0000-0000-0000-000000000001");
    public static readonly Guid DoctorId   = new("a1a1a1a1-0000-0000-0000-000000000002");
    public static readonly Guid NurseId    = new("a1a1a1a1-0000-0000-0000-000000000003");
    public static readonly Guid ReceptionistId = new("a1a1a1a1-0000-0000-0000-000000000004");
    public static readonly Guid PatientId  = new("a1a1a1a1-0000-0000-0000-000000000005");

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(r => r.Name)
            .IsUnique();

        builder.Property(r => r.Description)
            .HasMaxLength(200);

        builder.HasData(
            new Role { Id = AdminId,        Name = "Admin",        Description = "Full system access" },
            new Role { Id = DoctorId,       Name = "Doctor",       Description = "Clinical staff with patient access" },
            new Role { Id = NurseId,        Name = "Nurse",        Description = "Nursing staff with patient access" },
            new Role { Id = ReceptionistId, Name = "Receptionist", Description = "Front-desk appointment management" },
            new Role { Id = PatientId,      Name = "Patient",      Description = "Patient portal access" }
        );
    }
}
