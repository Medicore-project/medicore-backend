using MediCore.Billing.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Billing.Infrastructure.Persistence.Configurations;

public sealed class ServiceTariffConfiguration : IEntityTypeConfiguration<ServiceTariff>
{
    internal static readonly Guid GeneralConsultationId = Guid.Parse("8b4dcd4c-3cee-4fe6-a3d2-36c90cb0dd01");
    internal static readonly Guid SpecialistConsultationId = Guid.Parse("8b4dcd4c-3cee-4fe6-a3d2-36c90cb0dd02");
    internal static readonly Guid FollowUpId = Guid.Parse("8b4dcd4c-3cee-4fe6-a3d2-36c90cb0dd03");

    public void Configure(EntityTypeBuilder<ServiceTariff> builder)
    {
        builder.ToTable("service_tariffs");
        builder.HasKey(tariff => tariff.TariffId);
        builder.Property(tariff => tariff.ServiceCode).HasMaxLength(50).IsRequired();
        builder.Property(tariff => tariff.Description).HasMaxLength(300).IsRequired();
        builder.Property(tariff => tariff.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(tariff => tariff.Currency).HasMaxLength(3).IsRequired();
        builder.HasIndex(tariff => new { tariff.ServiceCode, tariff.EffectiveFromUtc })
            .IsUnique()
            .HasDatabaseName("ux_service_tariffs_code_effective_from");

        var effectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            new ServiceTariff
            {
                TariffId = GeneralConsultationId,
                ServiceCode = "GEN-CONSULT",
                Description = "General consultation",
                UnitPrice = 2500m,
                Currency = "LKR",
                EffectiveFromUtc = effectiveFrom,
                IsActive = true
            },
            new ServiceTariff
            {
                TariffId = SpecialistConsultationId,
                ServiceCode = "SPEC-CONSULT",
                Description = "Specialist consultation",
                UnitPrice = 5000m,
                Currency = "LKR",
                EffectiveFromUtc = effectiveFrom,
                IsActive = true
            },
            new ServiceTariff
            {
                TariffId = FollowUpId,
                ServiceCode = "FOLLOW-UP",
                Description = "Follow-up consultation",
                UnitPrice = 1500m,
                Currency = "LKR",
                EffectiveFromUtc = effectiveFrom,
                IsActive = true
            });
    }
}
