using MediCore.Billing.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Billing.Infrastructure.Persistence.Configurations;

public sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("notification_logs");
        builder.HasKey(log => log.NotificationLogId);
        builder.Property(log => log.EventType).HasMaxLength(100).IsRequired();
        builder.Property(log => log.TemplateCode).HasMaxLength(80).IsRequired();
        builder.Property(log => log.Recipient).HasMaxLength(320).IsRequired();
        builder.Property(log => log.Subject).HasMaxLength(300).IsRequired();
        builder.Property(log => log.Body).HasMaxLength(10_000).IsRequired();
        builder.Property(log => log.Status).HasMaxLength(20).IsRequired();
        builder.Property(log => log.CorrelationId).HasMaxLength(200).IsRequired();
        builder.Property(log => log.Error).HasMaxLength(2_000);
        builder.HasIndex(log => new { log.SourceMessageId, log.TemplateCode })
            .IsUnique()
            .HasDatabaseName("ux_notification_logs_source_template");
        builder.HasIndex(log => new { log.Status, log.CreatedAtUtc })
            .HasDatabaseName("ix_notification_logs_status_created");
        builder.HasOne(log => log.Template)
            .WithMany()
            .HasForeignKey(log => log.NotificationTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
