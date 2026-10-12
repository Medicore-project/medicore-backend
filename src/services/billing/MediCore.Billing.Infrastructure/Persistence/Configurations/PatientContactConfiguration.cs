using MediCore.Billing.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Billing.Infrastructure.Persistence.Configurations;

public sealed class PatientContactConfiguration : IEntityTypeConfiguration<PatientContact>
{
    public void Configure(EntityTypeBuilder<PatientContact> builder)
    {
        builder.ToTable("patient_contacts");
        builder.HasKey(contact => contact.PatientId);
        builder.Property(contact => contact.FullName).HasMaxLength(200).IsRequired();
        builder.Property(contact => contact.Email).HasMaxLength(320).IsRequired();
    }
}
