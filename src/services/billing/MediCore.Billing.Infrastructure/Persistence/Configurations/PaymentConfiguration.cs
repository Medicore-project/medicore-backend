using MediCore.Billing.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Billing.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", table => table.HasCheckConstraint(
            "ck_payments_amount_positive",
            "\"Amount\" > 0"));
        builder.HasKey(payment => payment.PaymentId);
        builder.Property(payment => payment.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(payment => payment.Method).HasMaxLength(20).IsRequired();
        builder.Property(payment => payment.RecordedBy).HasMaxLength(200).IsRequired();

        builder.HasOne(payment => payment.Invoice)
            .WithMany(invoice => invoice.Payments)
            .HasForeignKey(payment => payment.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(payment => new { payment.InvoiceId, payment.RecordedAtUtc })
            .HasDatabaseName("ix_payments_invoice_recorded_at");
        builder.HasIndex(payment => payment.RecordedAtUtc)
            .HasDatabaseName("ix_payments_recorded_at");
    }
}
