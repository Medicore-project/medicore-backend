using MediCore.Patient.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Patient.Infrastructure.Persistence.Configurations;

public sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages");
        builder.HasKey(message => message.MessageId)
            .HasName("pk_processed_messages");

        builder.Property(message => message.EventType).HasMaxLength(200).IsRequired();
        builder.Property(message => message.ConsumerGroup).HasMaxLength(100).IsRequired();
        builder.Property(message => message.SourceTopic).HasMaxLength(200).IsRequired();
        builder.Property(message => message.ProcessedAtUtc).IsRequired();

        builder.HasIndex(message => new { message.ConsumerGroup, message.ProcessedAtUtc })
            .HasDatabaseName("ix_processed_messages_consumer_processed_at");
    }
}
