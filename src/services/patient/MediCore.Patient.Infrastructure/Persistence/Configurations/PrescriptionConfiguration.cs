using MediCore.Patient.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="Prescription"/> entity.
/// Table name, column constraints, indexes and relationships all follow the
/// conventions established by <see cref="MedicalRecordConfiguration"/>.
/// </summary>
public sealed class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.ToTable("prescriptions");
        builder.HasKey(p => p.Id);

        // ── Business key ─────────────────────────────────────────────────────
        builder.Property(p => p.PrescriptionId).IsRequired();

        // ── Patient / visit linkage ──────────────────────────────────────────
        builder.Property(p => p.PatientId).IsRequired();
        builder.Property(p => p.MedicalRecordId);  // nullable — optional link

        // ── Clinical payload ─────────────────────────────────────────────────
        builder.Property(p => p.Drug).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Dosage).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Frequency).HasMaxLength(100).IsRequired();
        builder.Property(p => p.DurationDays).IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(2000);

        // ── Status / lifecycle ───────────────────────────────────────────────
        builder.Property(p => p.Status)
            .HasMaxLength(20)
            .HasDefaultValue(PrescriptionStatus.Active)
            .IsRequired();
        builder.Property(p => p.CompletedAtUtc);            // nullable
        builder.Property(p => p.PrescribedAtUtc).IsRequired();

        // ── Prescriber identity (denormalised from JWT) ───────────────────────
        builder.Property(p => p.PrescriberClinicianId).HasMaxLength(100).IsRequired();
        builder.Property(p => p.PrescriberClinicianEmail).HasMaxLength(256).IsRequired();
        builder.Property(p => p.PrescriberClinicianRole).HasMaxLength(50).IsRequired();

        // ── Audit columns ────────────────────────────────────────────────────
        builder.Property(p => p.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(p => p.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(p => p.UpdatedBy).HasMaxLength(100);

        // ── Relationships ────────────────────────────────────────────────────

        // Every prescription must belong to an existing patient.
        builder.HasOne<PatientEntity>()
            .WithMany()
            .HasForeignKey(p => p.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optionally linked to a medical-record visit.
        builder.HasOne<MedicalRecord>()
            .WithMany()
            .HasForeignKey(p => p.MedicalRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes ──────────────────────────────────────────────────────────

        // Unique business key — one row per PrescriptionId (no versioning pattern).
        builder.HasIndex(p => p.PrescriptionId)
            .IsUnique()
            .HasDatabaseName("ux_prescriptions_prescription_id");

        // Partial index optimises the common query "give me all active prescriptions
        // for patient X" — the WHERE clause is evaluated at index scan time.
        builder.HasIndex(p => new { p.PatientId, p.Status })
            .HasDatabaseName("ix_prescriptions_patient_status");

        // Full timeline ordering: newest-first list per patient.
        builder.HasIndex(p => new { p.PatientId, p.PrescribedAtUtc })
            .HasDatabaseName("ix_prescriptions_patient_prescribed_at");

        // ── Global query filter (soft-delete) ────────────────────────────────
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
