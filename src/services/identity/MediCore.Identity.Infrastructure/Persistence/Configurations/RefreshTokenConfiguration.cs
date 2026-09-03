using MediCore.Identity.Application.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Identity.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.Token)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(rt => rt.Token)
            .IsUnique();

        builder.Property(rt => rt.CreatedByIp)
            .HasMaxLength(45);

        builder.Property(rt => rt.RevokedByIp)
            .HasMaxLength(45);

        builder.HasOne(rt => rt.User)
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
