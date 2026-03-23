using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Data.Configurations;

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("webhook_deliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(d => d.EndpointId).HasColumnName("endpoint_id");
        builder.Property(d => d.AlertId).HasColumnName("alert_id");
        builder.Property(d => d.Status).HasColumnName("status");
        builder.Property(d => d.ResponseCode).HasColumnName("response_code");
        builder.Property(d => d.LatencyMs).HasColumnName("latency_ms");
        builder.Property(d => d.AttemptCount).HasColumnName("attempt_count");
        builder.Property(d => d.LastAttemptAt).HasColumnName("last_attempt_at");

        builder.HasOne(d => d.Endpoint).WithMany().HasForeignKey(d => d.EndpointId);
        builder.HasIndex(d => new { d.EndpointId, d.Status });
    }
}
