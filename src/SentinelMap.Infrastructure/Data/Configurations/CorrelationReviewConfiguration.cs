using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Data.Configurations;

public class CorrelationReviewConfiguration : IEntityTypeConfiguration<CorrelationReview>
{
    public void Configure(EntityTypeBuilder<CorrelationReview> builder)
    {
        builder.ToTable("correlation_reviews");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.SourceEntityId).HasColumnName("source_entity_id");
        builder.Property(r => r.TargetEntityId).HasColumnName("target_entity_id");
        builder.Property(r => r.Confidence).HasColumnName("confidence");
        builder.Property(r => r.RuleScores).HasColumnName("rule_scores").HasColumnType("jsonb");
        builder.Property(r => r.Status).HasColumnName("status");
        builder.Property(r => r.ReviewedBy).HasColumnName("reviewed_by");
        builder.Property(r => r.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(r => r.Status);

        builder.HasOne(r => r.SourceEntity).WithMany().HasForeignKey(r => r.SourceEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.TargetEntity).WithMany().HasForeignKey(r => r.TargetEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}
