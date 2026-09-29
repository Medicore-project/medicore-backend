using MediCore.Appointment.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Appointment.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="DoctorLeave"/>.
/// </summary>
public sealed class DoctorLeaveConfiguration : IEntityTypeConfiguration<DoctorLeave>
{
    public void Configure(EntityTypeBuilder<DoctorLeave> builder)
    {
        builder.ToTable("doctor_leaves");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.LeaveId).IsRequired();

        // No FK on DoctorId — DoctorCache is eventually consistent; see DoctorLeave.DoctorId.
        builder.Property(l => l.DoctorId).IsRequired();

        builder.Property(l => l.StartDate).HasColumnType("date").IsRequired();
        builder.Property(l => l.EndDate).HasColumnType("date").IsRequired();
        builder.Property(l => l.Reason).HasMaxLength(500);

        // ── Approval workflow ────────────────────────────────────────────────
        // Defaulting a string is safe, unlike defaulting a bool: EF only omits a value from the
        // INSERT when it equals the CLR default, and "Pending" never equals null.
        builder.Property(l => l.Status)
            .HasMaxLength(20)
            .HasDefaultValue(LeaveStatus.Pending)
            .IsRequired();
        builder.Property(l => l.ReviewedBy).HasMaxLength(100);
        builder.Property(l => l.ReviewedAtUtc);
        builder.Property(l => l.ReviewNotes).HasMaxLength(500);

        builder.Property(l => l.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(l => l.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(l => l.UpdatedBy).HasMaxLength(100);

        builder.HasIndex(l => l.LeaveId)
            .IsUnique()
            .HasDatabaseName("ux_doctor_leaves_leave_id");

        // Slot generation asks "is this doctor on approved leave on this date" for every candidate
        // day. Status leads the date columns because generation and the approval queue both filter
        // on it first, and it is far more selective than DoctorId once the queue is small.
        builder.HasIndex(l => new { l.DoctorId, l.Status, l.StartDate, l.EndDate })
            .HasDatabaseName("ix_doctor_leaves_doctor_status_dates");

        // The approval queue: pending requests across all doctors, soonest first.
        builder.HasIndex(l => new { l.Status, l.StartDate })
            .HasDatabaseName("ix_doctor_leaves_status_start");

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}
