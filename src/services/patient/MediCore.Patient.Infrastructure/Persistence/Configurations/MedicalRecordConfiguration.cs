using MediCore.Patient.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Infrastructure.Persistence.Configurations;

public sealed class MedicalRecordConfiguration : IEntityTypeConfiguration<MedicalRecord>
{
    public void Configure(EntityTypeBuilder<MedicalRecord> builder)
    {
        builder.ToTable("medical_records");
        builder.HasKey(record => record.Id);

        builder.Property(record => record.RecordId).IsRequired();
        builder.Property(record => record.PatientId).IsRequired();
        builder.Property(record => record.VisitReference).IsRequired();
        builder.Property(record => record.ClinicalNotes).HasMaxLength(8000).IsRequired();
        builder.Property(record => record.AuthorClinicianId).HasMaxLength(100).IsRequired();
        builder.Property(record => record.AuthorClinicianEmail).HasMaxLength(256).IsRequired();
        builder.Property(record => record.AuthorClinicianRole).HasMaxLength(50).IsRequired();
        builder.Property(record => record.AuthoredAtUtc).IsRequired();
        builder.Property(record => record.Version).HasDefaultValue(1).IsRequired();
        builder.Property(record => record.IsCurrent).HasDefaultValue(true).IsRequired();
        builder.Property(record => record.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(record => record.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(record => record.UpdatedBy).HasMaxLength(100);

        builder.HasOne<PatientEntity>()
            .WithMany()
            .HasForeignKey(record => record.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(record => record.PreviousVersion)
            .WithMany()
            .HasForeignKey(record => record.PreviousVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(record => record.Conditions)
            .WithOne(condition => condition.MedicalRecord)
            .HasForeignKey(condition => condition.MedicalRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(record => new { record.RecordId, record.Version })
            .IsUnique()
            .HasDatabaseName("ux_medical_records_record_version");

        builder.HasIndex(record => record.RecordId)
            .IsUnique()
            .HasFilter("\"IsCurrent\" = true AND \"IsDeleted\" = false")
            .HasDatabaseName("ux_medical_records_current_record");

        builder.HasIndex(record => new { record.PatientId, record.IsCurrent, record.AuthoredAtUtc })
            .HasDatabaseName("ix_medical_records_patient_current_authored");

        builder.HasIndex(record => record.VisitReference)
            .HasDatabaseName("ix_medical_records_visit_reference");

        builder.HasQueryFilter(record => !record.IsDeleted);
    }
}
