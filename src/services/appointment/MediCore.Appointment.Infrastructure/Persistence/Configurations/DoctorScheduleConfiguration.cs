using MediCore.Appointment.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Appointment.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="DoctorSchedule"/>.
/// Follows the table, index and soft-delete conventions established by the Patient service.
/// </summary>
public sealed class DoctorScheduleConfiguration : IEntityTypeConfiguration<DoctorSchedule>
{
    public void Configure(EntityTypeBuilder<DoctorSchedule> builder)
    {
        builder.ToTable("doctor_schedules");
        builder.HasKey(s => s.Id);

        // ── Business key ─────────────────────────────────────────────────────
        builder.Property(s => s.ScheduleId).IsRequired();

        // ── Doctor linkage (no FK — DoctorCache arrives in SCRUM-33) ─────────
        builder.Property(s => s.DoctorId).IsRequired();

        // ── Working window ───────────────────────────────────────────────────
        builder.Property(s => s.DayOfWeek).IsRequired();
        builder.Property(s => s.StartTime).HasColumnType("time").IsRequired();
        builder.Property(s => s.EndTime).HasColumnType("time").IsRequired();
        builder.Property(s => s.SlotDurationMinutes).IsRequired();

        // ── Effective period ─────────────────────────────────────────────────
        builder.Property(s => s.EffectiveFrom).HasColumnType("date").IsRequired();
        builder.Property(s => s.EffectiveTo).HasColumnType("date");
        // Deliberately no HasDefaultValue(true): that marks the property ValueGenerated.OnAdd,
        // and EF then omits any value equal to the CLR default (false) from the INSERT — so a
        // schedule created as inactive would silently be stored as active.
        builder.Property(s => s.IsActive).IsRequired();

        // ── Audit columns ────────────────────────────────────────────────────
        builder.Property(s => s.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(s => s.UpdatedBy).HasMaxLength(100);

        // ── Indexes ──────────────────────────────────────────────────────────

        // Unique business key — one row per ScheduleId.
        builder.HasIndex(s => s.ScheduleId)
            .IsUnique()
            .HasDatabaseName("ux_doctor_schedules_schedule_id");

        // Drives the AC4 overlap check and the weekly-grid read: every live schedule for a
        // doctor on a given weekday.
        builder.HasIndex(s => new { s.DoctorId, s.DayOfWeek })
            .HasDatabaseName("ix_doctor_schedules_doctor_day");

        // ── Global query filter (soft-delete) ────────────────────────────────
        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
