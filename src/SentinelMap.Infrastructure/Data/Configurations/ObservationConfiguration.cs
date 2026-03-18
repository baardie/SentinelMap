using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Data.Configurations;

public class ObservationConfiguration : IEntityTypeConfiguration<Observation>
{
    public void Configure(EntityTypeBuilder<Observation> builder)
    {
        builder.ToTable("observations");
        builder.HasKey(o => new { o.Id, o.ObservedAt });
        builder.Property(o => o.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(o => o.EntityId).HasColumnName("entity_id");
        builder.Property(o => o.SourceType).HasColumnName("source_type");
        builder.Property(o => o.ExternalId).HasColumnName("external_id");
        builder.Property(o => o.Position).HasColumnName("position").HasColumnType("geometry(Point, 4326)");
        builder.Property(o => o.SpeedMps).HasColumnName("speed_mps");
        builder.Property(o => o.Heading).HasColumnName("heading");
        builder.Property(o => o.RawData).HasColumnName("raw_data").HasColumnType("jsonb");
        builder.Property(o => o.ObservedAt).HasColumnName("observed_at");
        builder.Property(o => o.IngestedAt).HasColumnName("ingested_at");

        builder.HasIndex(o => o.Position).HasMethod("gist");
        builder.HasIndex(o => new { o.EntityId, o.ObservedAt }).IsDescending(false, true);

        builder.HasOne(o => o.Entity).WithMany().HasForeignKey(o => o.EntityId);
    }
}
