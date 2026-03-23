using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.UserId).HasColumnName("user_id");
        builder.Property(r => r.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.Property(r => r.FamilyId).HasColumnName("family_id").IsRequired();
        builder.Property(r => r.DeviceInfo).HasColumnName("device_info");
        builder.Property(r => r.IsRevoked).HasColumnName("is_revoked");
        builder.Property(r => r.ExpiresAt).HasColumnName("expires_at");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.LastUsedAt).HasColumnName("last_used_at");

        builder.HasIndex(r => r.UserId).HasDatabaseName("idx_refresh_tokens_user_id");
        builder.HasIndex(r => r.TokenHash).IsUnique().HasDatabaseName("idx_refresh_tokens_token_hash");
        builder.HasIndex(r => r.FamilyId).HasDatabaseName("idx_refresh_tokens_family_id");
    }
}
