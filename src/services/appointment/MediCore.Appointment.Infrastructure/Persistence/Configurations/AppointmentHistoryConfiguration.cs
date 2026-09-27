using MediCore.Appointment.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Appointment.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="AppointmentHistoryEntry"/>.
/// </summary>
/// <remarks>
/// No soft-delete filter and no audit columns: the table is append-only, as
/// <c>outbox_messages</c> is. No foreign key to <c>appointments</c> either — the entry refers to
/// the business key, which the appointments table keeps unique but is not its primary key.
/// </remarks>
public sealed class AppointmentHistoryConfiguration : IEntityTypeConfiguration<AppointmentHistoryEntry>
{
    public void Configure(EntityTypeBuilder<AppointmentHistoryEntry> builder)
    {
        builder.ToTable("appointment_history");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.AppointmentId).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(20).IsRequired();
        builder.Property(entry => entry.FromStatus).HasMaxLength(20);
        builder.Property(entry => entry.ToStatus).HasMaxLength(20).IsRequired();
        builder.Property(entry => entry.Reason).HasMaxLength(500);
        builder.Property(entry => entry.Actor).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.OccurredAtUtc).IsRequired();

        // "The history of this appointment, in order" is the only question asked of this table.
        builder.HasIndex(entry => new { entry.AppointmentId, entry.OccurredAtUtc })
            .HasDatabaseName("ix_appointment_history_appointment_occurred");
    }
}
