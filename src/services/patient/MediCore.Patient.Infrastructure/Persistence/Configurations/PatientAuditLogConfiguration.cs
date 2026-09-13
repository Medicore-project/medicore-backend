using MediCore.Patient.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Infrastructure.Persistence.Configurations;

public sealed class PatientAuditLogConfiguration : IEntityTypeConfiguration<PatientAuditLog>
{
    public void Configure(EntityTypeBuilder<PatientAuditLog> builder)
    {
        builder.ToTable("patient_audit_logs");
        builder.HasKey(audit => audit.Id);

        builder.Property(audit => audit.ActorId).HasMaxLength(100).IsRequired();
        builder.Property(audit => audit.ActorRole).HasMaxLength(50).IsRequired();
        builder.Property(audit => audit.Action).HasMaxLength(100).IsRequired();
        builder.Property(audit => audit.CorrelationId).HasMaxLength(200).IsRequired();
        builder.Property(audit => audit.IpAddress).HasMaxLength(64);
        builder.Property(audit => audit.OccurredAtUtc).IsRequired();

        builder.HasIndex(audit => new { audit.PatientId, audit.OccurredAtUtc });
        builder.HasIndex(audit => audit.ActorId);

        builder.HasOne<PatientEntity>()
            .WithMany()
            .HasForeignKey(audit => audit.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
