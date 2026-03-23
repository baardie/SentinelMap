using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Data.Configurations;

public class GeofenceConfiguration : IEntityTypeConfiguration<Geofence>
{
    public void Configure(EntityTypeBuilder<Geofence> builder)
    {
        builder.ToTable("geofences");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id");
        builder.Property(g => g.Name).HasColumnName("name");
        builder.Property(g => g.Geometry).HasColumnName("geometry").HasColumnType("geometry(Polygon, 4326)");
        builder.Property(g => g.FenceType).HasColumnName("fence_type");
        builder.Property(g => g.Classification).HasColumnName("classification").HasConversion<string>();
        builder.Property(g => g.CreatedBy).HasColumnName("created_by");
        builder.Property(g => g.Color).HasColumnName("color");
        builder.Property(g => g.IsActive).HasColumnName("is_active");
        builder.Property(g => g.CreatedAt).HasColumnName("created_at");
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(g => g.Geometry).HasMethod("gist");
    }
}
