using MediCore.Patient.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Patient.Infrastructure.Persistence.Configurations;

public sealed class ConditionConfiguration : IEntityTypeConfiguration<Condition>
{
    public void Configure(EntityTypeBuilder<Condition> builder)
    {
        builder.ToTable("conditions");
        builder.HasKey(condition => condition.Id);

        builder.Property(condition => condition.MedicalRecordId).IsRequired();
        builder.Property(condition => condition.Name).HasMaxLength(200).IsRequired();
        builder.Property(condition => condition.Code).HasMaxLength(50);
        builder.Property(condition => condition.ClinicalStatus).HasMaxLength(30).IsRequired();
        builder.Property(condition => condition.Notes).HasMaxLength(2000);
        builder.Property(condition => condition.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(condition => condition.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(condition => condition.UpdatedBy).HasMaxLength(100);

        builder.HasIndex(condition => condition.MedicalRecordId)
            .HasDatabaseName("ix_conditions_medical_record_id");

        builder.HasQueryFilter(condition => !condition.IsDeleted);
    }
}
