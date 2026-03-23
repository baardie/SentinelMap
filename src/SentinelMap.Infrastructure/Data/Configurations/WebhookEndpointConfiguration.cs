using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Data.Configurations;

public class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("id");
        builder.Property(w => w.Url).HasColumnName("url").IsRequired();
        builder.Property(w => w.Secret).HasColumnName("secret").IsRequired();
        builder.Property(w => w.EventFilter).HasColumnName("event_filter").HasColumnType("jsonb");
        builder.Property(w => w.IsActive).HasColumnName("is_active");
        builder.Property(w => w.ConsecutiveFailures).HasColumnName("consecutive_failures");
        builder.Property(w => w.CreatedBy).HasColumnName("created_by");
        builder.Property(w => w.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(w => w.IsActive);
    }
}
