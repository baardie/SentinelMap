using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Data.Configurations;

public class MapFeatureConfiguration : IEntityTypeConfiguration<MapFeature>
{
    public void Configure(EntityTypeBuilder<MapFeature> builder)
    {
        builder.ToTable("map_features");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");
        builder.Property(f => f.FeatureType).HasColumnName("feature_type");
        builder.Property(f => f.Name).HasColumnName("name");
        builder.Property(f => f.Position).HasColumnName("position").HasColumnType("geometry(Point, 4326)");
        builder.Property(f => f.Icon).HasColumnName("icon");
        builder.Property(f => f.Color).HasColumnName("color");
        builder.Property(f => f.Details).HasColumnName("details").HasColumnType("jsonb");
        builder.Property(f => f.Source).HasColumnName("source");
        builder.Property(f => f.IsActive).HasColumnName("is_active");
        builder.Property(f => f.CreatedBy).HasColumnName("created_by");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(f => f.Position).HasMethod("gist");
        builder.HasIndex(f => f.FeatureType);
        builder.HasIndex(f => f.Source);
    }
}
