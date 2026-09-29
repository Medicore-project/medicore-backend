using MediCore.Appointment.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Appointment.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core fluent configuration for <see cref="PublicHoliday"/>.
/// </summary>
public sealed class PublicHolidayConfiguration : IEntityTypeConfiguration<PublicHoliday>
{
    public void Configure(EntityTypeBuilder<PublicHoliday> builder)
    {
        builder.ToTable("public_holidays");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.HolidayId).IsRequired();
        builder.Property(h => h.Date).HasColumnType("date").IsRequired();
        builder.Property(h => h.Name).HasMaxLength(200).IsRequired();

        builder.Property(h => h.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(h => h.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(h => h.UpdatedBy).HasMaxLength(100);

        builder.HasIndex(h => h.HolidayId)
            .IsUnique()
            .HasDatabaseName("ux_public_holidays_holiday_id");

        // One live holiday per date. Filtered on IsDeleted so that removing a holiday and
        // re-adding the same date later does not hit a stale constraint.
        builder.HasIndex(h => h.Date)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false")
            .HasDatabaseName("ux_public_holidays_date");

        builder.HasQueryFilter(h => !h.IsDeleted);
    }
}
