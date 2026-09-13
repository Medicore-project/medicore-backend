using MediCore.Patient.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Infrastructure.Persistence.Configurations;

public sealed class PatientConfiguration : IEntityTypeConfiguration<PatientEntity>
{
    public void Configure(EntityTypeBuilder<PatientEntity> builder)
    {
        builder.ToTable("patients");
        builder.HasKey(patient => patient.Id);

        builder.Property(patient => patient.PatientNumber)
            .HasMaxLength(20)
            .HasDefaultValueSql("'PAT-' || LPAD(nextval('medicore_patient.patient_number_seq')::text, 6, '0')")
            .ValueGeneratedOnAdd();

        builder.Property(patient => patient.Nic).HasMaxLength(12).IsRequired();
        builder.Property(patient => patient.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(patient => patient.LastName).HasMaxLength(100).IsRequired();
        builder.Property(patient => patient.DateOfBirth).HasColumnType("date").IsRequired();
        builder.Property(patient => patient.Gender).HasMaxLength(30).IsRequired();
        builder.Property(patient => patient.Email).HasMaxLength(256).IsRequired();
        builder.Property(patient => patient.Phone).HasMaxLength(20).IsRequired();
        builder.Property(patient => patient.AddressLine1).HasMaxLength(200).IsRequired();
        builder.Property(patient => patient.AddressLine2).HasMaxLength(200);
        builder.Property(patient => patient.District).HasMaxLength(100).IsRequired();
        builder.Property(patient => patient.EmergencyContactName).HasMaxLength(200);
        builder.Property(patient => patient.EmergencyContactPhone).HasMaxLength(20);
        builder.Property(patient => patient.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(patient => patient.UpdatedBy).HasMaxLength(100);

        builder.HasIndex(patient => patient.Nic)
            .IsUnique()
            .HasDatabaseName("ux_patients_nic");

        builder.HasIndex(patient => patient.PatientNumber)
            .IsUnique()
            .HasDatabaseName("ux_patients_patient_number");

        builder.HasIndex(patient => patient.LastName);
        builder.HasIndex(patient => patient.District);
        builder.HasQueryFilter(patient => !patient.IsDeleted);
    }
}
