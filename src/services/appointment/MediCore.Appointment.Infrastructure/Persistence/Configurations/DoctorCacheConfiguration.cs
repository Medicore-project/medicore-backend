using MediCore.Appointment.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Appointment.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="DoctorCache"/>.
/// </summary>
public sealed class DoctorCacheConfiguration : IEntityTypeConfiguration<DoctorCache>
{
    public void Configure(EntityTypeBuilder<DoctorCache> builder)
    {
        builder.ToTable("cached_doctors");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.DoctorId).IsRequired();
        builder.Property(d => d.FullName).HasMaxLength(200).IsRequired();
        // Matches StaffProfile.Specialization's length in Identity.
        builder.Property(d => d.Specialization).HasMaxLength(100).IsRequired();
        builder.Property(d => d.DepartmentId).IsRequired();

        // No HasDefaultValue(true): EF would then omit IsActive = false from the INSERT and the row
        // would silently save as active. The consumer always sets it explicitly.
        builder.Property(d => d.IsActive).IsRequired();
        builder.Property(d => d.LastEventOccurredAtUtc).IsRequired();

        builder.Property(d => d.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(d => d.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(d => d.UpdatedBy).HasMaxLength(100);

        // One live row per doctor. Partial, so a soft-deleted row never blocks re-caching.
        builder.HasIndex(d => d.DoctorId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("ux_cached_doctors_doctor_id");

        // The bookable-doctors listing: active only, optionally by specialization.
        builder.HasIndex(d => new { d.IsActive, d.Specialization })
            .HasDatabaseName("ix_cached_doctors_active_specialization");

        builder.HasQueryFilter(d => !d.IsDeleted);
    }
}
