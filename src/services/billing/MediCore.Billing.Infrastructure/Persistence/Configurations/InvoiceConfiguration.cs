using MediCore.Billing.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Billing.Infrastructure.Persistence.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");
        builder.HasKey(invoice => invoice.InvoiceId);
        builder.Property(invoice => invoice.InvoiceNumber).HasMaxLength(30).IsRequired();
        builder.Property(invoice => invoice.ServiceCode).HasMaxLength(50).IsRequired();
        builder.Property(invoice => invoice.Status).HasMaxLength(20).IsRequired();
        builder.Property(invoice => invoice.Currency).HasMaxLength(3).IsRequired();
        builder.Property(invoice => invoice.Subtotal).HasPrecision(18, 2).IsRequired();
        builder.Property(invoice => invoice.Total).HasPrecision(18, 2).IsRequired();
        builder.Property(invoice => invoice.PricingIssue).HasMaxLength(500);

        builder.HasIndex(invoice => invoice.InvoiceNumber)
            .IsUnique()
            .HasDatabaseName("ux_invoices_invoice_number");
        builder.HasIndex(invoice => invoice.AppointmentId)
            .IsUnique()
            .HasDatabaseName("ux_invoices_appointment_id");
        builder.HasIndex(invoice => new { invoice.PatientId, invoice.IssuedAtUtc })
            .HasDatabaseName("ix_invoices_patient_issued_at");
    }
}
