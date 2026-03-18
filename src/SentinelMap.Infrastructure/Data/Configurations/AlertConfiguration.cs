using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Data.Configurations;

public class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("alerts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.Type).HasColumnName("type").HasConversion<string>();
        builder.Property(a => a.Severity).HasColumnName("severity").HasConversion<string>();
        builder.Property(a => a.EntityId).HasColumnName("entity_id");
        builder.Property(a => a.GeofenceId).HasColumnName("geofence_id");
        builder.Property(a => a.Summary).HasColumnName("summary");
        builder.Property(a => a.Details).HasColumnName("details").HasColumnType("jsonb");
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(a => a.AcknowledgedBy).HasColumnName("acknowledged_by");
        builder.Property(a => a.AcknowledgedAt).HasColumnName("acknowledged_at");
        builder.Property(a => a.ResolvedBy).HasColumnName("resolved_by");
        builder.Property(a => a.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(a => a.Classification).HasColumnName("classification").HasConversion<string>();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(a => new { a.Status, a.Severity, a.CreatedAt }).IsDescending(false, false, true);
        builder.HasOne(a => a.Entity).WithMany(e => e.Alerts).HasForeignKey(a => a.EntityId);
        builder.HasOne(a => a.Geofence).WithMany().HasForeignKey(a => a.GeofenceId);
    }
}
