using MediCore.Appointment.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Appointment.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="Slot"/>.
/// </summary>
public sealed class SlotConfiguration : IEntityTypeConfiguration<Slot>
{
    /// <summary>
    /// Partial-index predicate backing SCRUM-32 AC5 (idempotent regeneration).
    /// <para>
    /// Soft-deleted rows are excluded so that a deleted slot never permanently blocks
    /// regeneration at the same instant. Flagged rows are excluded because a flagged slot is a
    /// historical record of a booking that no longer fits the schedule — if the schedule later
    /// covers that time again, a fresh bookable slot must be able to coexist with it.
    /// Available, Booked and Blocked all remain inside the uniqueness set, so regeneration can
    /// never produce a duplicate bookable slot.
    /// </para>
    /// </summary>
    internal const string DoctorStartUniqueFilter =
        "\"IsDeleted\" = false AND \"Status\" <> 'Flagged'";

    public void Configure(EntityTypeBuilder<Slot> builder)
    {
        builder.ToTable("slots");
        builder.HasKey(s => s.Id);

        // ── Business key ─────────────────────────────────────────────────────
        builder.Property(s => s.SlotId).IsRequired();

        // ── Doctor linkage (no FK — DoctorCache is eventually consistent) ────
        builder.Property(s => s.DoctorId).IsRequired();

        // ── Time window ──────────────────────────────────────────────────────
        builder.Property(s => s.StartUtc).IsRequired();
        builder.Property(s => s.EndUtc).IsRequired();
        builder.Property(s => s.SlotDate).HasColumnType("date").IsRequired();
        builder.Property(s => s.DurationMinutes).IsRequired();

        // ── Status / lifecycle ───────────────────────────────────────────────
        builder.Property(s => s.Status)
            .HasMaxLength(20)
            .HasDefaultValue(SlotStatus.Available)
            .IsRequired();
        builder.Property(s => s.FlaggedReason).HasMaxLength(500);
        builder.Property(s => s.FlaggedAtUtc);

        // ── Audit columns ────────────────────────────────────────────────────
        builder.Property(s => s.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(s => s.UpdatedBy).HasMaxLength(100);

        // ── Relationships ────────────────────────────────────────────────────

        // The generating schedule. Restrict, never cascade: removing a schedule must not delete
        // slots that may already carry bookings.
        builder.HasOne<DoctorSchedule>()
            .WithMany()
            .HasForeignKey(s => s.DoctorScheduleId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes ──────────────────────────────────────────────────────────

        // Unique business key — one row per SlotId.
        builder.HasIndex(s => s.SlotId)
            .IsUnique()
            .HasDatabaseName("ux_slots_slot_id");

        // SCRUM-32 AC5 — see DoctorStartUniqueFilter.
        builder.HasIndex(s => new { s.DoctorId, s.StartUtc })
            .IsUnique()
            .HasFilter(DoctorStartUniqueFilter)
            .HasDatabaseName("ux_slots_doctor_start");

        // The availability query: "open slots for doctor X between two dates".
        builder.HasIndex(s => new { s.DoctorId, s.SlotDate, s.Status })
            .HasDatabaseName("ix_slots_doctor_date_status");

        // Regeneration and schedule revision walk every slot a schedule produced.
        builder.HasIndex(s => s.DoctorScheduleId)
            .HasDatabaseName("ix_slots_schedule");

        // The "needs attention" list: flagged slots, soonest first.
        builder.HasIndex(s => new { s.Status, s.StartUtc })
            .HasDatabaseName("ix_slots_status_start");

        // ── Global query filter (soft-delete) ────────────────────────────────
        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
