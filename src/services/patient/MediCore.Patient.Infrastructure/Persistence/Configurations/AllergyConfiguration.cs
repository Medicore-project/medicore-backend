using MediCore.Patient.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PatientEntity = MediCore.Patient.Application.Entities.Patient;

namespace MediCore.Patient.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="Allergy"/> entity.
/// Table name, column constraints, indexes and relationship follow the
/// conventions established by <see cref="PrescriptionConfiguration"/>.
/// </summary>
public sealed class AllergyConfiguration : IEntityTypeConfiguration<Allergy>
{
    public void Configure(EntityTypeBuilder<Allergy> builder)
    {
        builder.ToTable("allergies");
        builder.HasKey(a => a.Id);

        // ── Business key ──────────────────────────────────────────────────────
        builder.Property(a => a.AllergyId).IsRequired();

        // ── Patient linkage ───────────────────────────────────────────────────
        builder.Property(a => a.PatientId).IsRequired();

        // ── Clinical payload ──────────────────────────────────────────────────
        builder.Property(a => a.Allergen).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Severity).HasMaxLength(30).IsRequired();
        builder.Property(a => a.Reaction).HasMaxLength(500);   // nullable
        builder.Property(a => a.Notes).HasMaxLength(2000);     // nullable

        // ── Status / lifecycle ────────────────────────────────────────────────
        builder.Property(a => a.Status)
            .HasMaxLength(20)
            .HasDefaultValue(AllergyStatus.Active)
            .IsRequired();

        builder.Property(a => a.RecordedAtUtc).IsRequired();

        // ── Recording clinician identity (denormalised from JWT) ──────────────
        builder.Property(a => a.RecordedByClinicianId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.RecordedByClinicianEmail).HasMaxLength(256).IsRequired();
        builder.Property(a => a.RecordedByClinicianRole).HasMaxLength(50).IsRequired();

        // ── Audit columns ─────────────────────────────────────────────────────
        builder.Property(a => a.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(a => a.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(a => a.UpdatedBy).HasMaxLength(100);

        // ── Relationship ──────────────────────────────────────────────────────

        // Every allergy must belong to an existing patient.
        builder.HasOne<PatientEntity>()
            .WithMany()
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Unique business key — one row per AllergyId.
        builder.HasIndex(a => a.AllergyId)
            .IsUnique()
            .HasDatabaseName("ux_allergies_allergy_id");

        // Fast-path for the most common query: all active allergies for a patient.
        // Also used by the conflict check during prescription creation.
        builder.HasIndex(a => new { a.PatientId, a.Status })
            .HasDatabaseName("ix_allergies_patient_status");

        // Timeline ordering: newest-first list per patient.
        builder.HasIndex(a => new { a.PatientId, a.RecordedAtUtc })
            .HasDatabaseName("ix_allergies_patient_recorded_at");

        // ── Global query filter (soft-delete) ─────────────────────────────────
        builder.HasQueryFilter(a => !a.IsDeleted);
    }
}
