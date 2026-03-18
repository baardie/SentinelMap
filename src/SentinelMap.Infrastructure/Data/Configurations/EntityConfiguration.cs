using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Data.Configurations;

public class EntityConfiguration : IEntityTypeConfiguration<TrackedEntity>
{
    public void Configure(EntityTypeBuilder<TrackedEntity> builder)
    {
        builder.ToTable("entities");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Type).HasColumnName("type").HasConversion<string>();
        builder.Property(e => e.DisplayName).HasColumnName("display_name");
        builder.Property(e => e.LastKnownPosition).HasColumnName("last_known_position").HasColumnType("geometry(Point, 4326)");
        builder.Property(e => e.LastSpeedMps).HasColumnName("last_speed_mps");
        builder.Property(e => e.LastHeading).HasColumnName("last_heading");
        builder.Property(e => e.LastSeen).HasColumnName("last_seen");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(e => e.Classification).HasColumnName("classification").HasConversion<string>();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.LastKnownPosition).HasMethod("gist");
        builder.HasIndex(e => new { e.Status, e.LastSeen });
    }
}
