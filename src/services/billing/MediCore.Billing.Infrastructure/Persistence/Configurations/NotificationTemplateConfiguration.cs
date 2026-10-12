using MediCore.Billing.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Billing.Infrastructure.Persistence.Configurations;

public sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public static readonly Guid WelcomeId = Guid.Parse("9c5dcd4c-3cee-4fe6-a3d2-36c90cb0ee01");
    public static readonly Guid AppointmentId = Guid.Parse("9c5dcd4c-3cee-4fe6-a3d2-36c90cb0ee02");
    public static readonly Guid ReceiptId = Guid.Parse("9c5dcd4c-3cee-4fe6-a3d2-36c90cb0ee03");

    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates");
        builder.HasKey(template => template.NotificationTemplateId);
        builder.Property(template => template.Code).HasMaxLength(80).IsRequired();
        builder.Property(template => template.Name).HasMaxLength(150).IsRequired();
        builder.Property(template => template.SubjectTemplate).HasMaxLength(300).IsRequired();
        builder.Property(template => template.BodyTemplate).HasMaxLength(10_000).IsRequired();
        builder.HasIndex(template => template.Code)
            .IsUnique()
            .HasDatabaseName("ux_notification_templates_code");

        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            new NotificationTemplate
            {
                NotificationTemplateId = WelcomeId,
                Code = "PATIENT_WELCOME",
                Name = "Patient welcome",
                SubjectTemplate = "Welcome to MediCore, {{patientName}}",
                BodyTemplate = "Hello {{patientName}},\n\nYour MediCore patient profile is ready. Your patient reference is {{patientId}}.",
                IsActive = true,
                CreatedAtUtc = createdAt
            },
            new NotificationTemplate
            {
                NotificationTemplateId = AppointmentId,
                Code = "APPOINTMENT_CONFIRMATION",
                Name = "Appointment confirmation",
                SubjectTemplate = "MediCore appointment confirmed",
                BodyTemplate = "Hello {{patientName}},\n\nYour {{serviceCode}} appointment is confirmed for {{slotStart}}. Appointment reference: {{appointmentId}}.",
                IsActive = true,
                CreatedAtUtc = createdAt
            },
            new NotificationTemplate
            {
                NotificationTemplateId = ReceiptId,
                Code = "PAYMENT_RECEIPT",
                Name = "Payment receipt",
                SubjectTemplate = "MediCore payment receipt",
                BodyTemplate = "Hello {{patientName}},\n\nWe received {{amount}} LKR by {{method}} for invoice {{invoiceId}}. Thank you.",
                IsActive = true,
                CreatedAtUtc = createdAt
            });
    }
}
