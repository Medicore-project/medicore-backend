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

        // No FK on DoctorId — DoctorCache arrives in SCRUM-33.
        builder.Property(l => l.DoctorId).IsRequired();

        builder.Property(l => l.StartDate).HasColumnType("date").IsRequired();
        builder.Property(l => l.EndDate).HasColumnType("date").IsRequired();
        builder.Property(l => l.Reason).HasMaxLength(500);

        builder.Property(l => l.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(l => l.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(l => l.UpdatedBy).HasMaxLength(100);

        builder.HasIndex(l => l.LeaveId)
            .IsUnique()
            .HasDatabaseName("ux_doctor_leaves_leave_id");

        // Slot generation asks "is this doctor on leave on this date" for every candidate day.
        builder.HasIndex(l => new { l.DoctorId, l.StartDate, l.EndDate })
            .HasDatabaseName("ix_doctor_leaves_doctor_dates");

        builder.HasQueryFilter(l => !l.IsDeleted);
    }
}
