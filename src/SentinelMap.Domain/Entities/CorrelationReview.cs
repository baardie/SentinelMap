namespace SentinelMap.Domain.Entities;

public class CorrelationReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceEntityId { get; set; }
    public Guid TargetEntityId { get; set; }
    public double Confidence { get; set; }
    public string? RuleScores { get; set; }  // JSONB — serialized list of CorrelationScore
    public string Status { get; set; } = "Pending";  // Pending, Approved, Rejected
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TrackedEntity SourceEntity { get; set; } = null!;
    public TrackedEntity TargetEntity { get; set; } = null!;
}
