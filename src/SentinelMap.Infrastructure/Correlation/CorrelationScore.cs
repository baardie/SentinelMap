using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Correlation;

/// <summary>
/// Result of a single correlation rule evaluation.
/// </summary>
public record CorrelationScore(string RuleId, double Confidence, string Reason);

/// <summary>
/// A rule that evaluates whether an incoming observation matches an existing entity.
/// </summary>
public interface ICorrelationRule
{
    string RuleId { get; }

    Task<CorrelationScore?> EvaluateAsync(
        string sourceType,
        string externalId,
        string? displayName,
        Point position,
        TrackedEntity candidate,
        CancellationToken ct = default);
}
