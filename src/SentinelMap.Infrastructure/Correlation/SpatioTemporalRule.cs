using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Correlation;

/// <summary>
/// Speed-scaled spatial proximity rule. Calculates a dynamic search radius based on
/// the candidate's last speed and time since last seen.
/// </summary>
public class SpatioTemporalRule : ICorrelationRule
{
    public string RuleId => "SpatioTemporal";

    public Task<CorrelationScore?> EvaluateAsync(
        string sourceType,
        string externalId,
        string? displayName,
        Point position,
        TrackedEntity candidate,
        CancellationToken ct = default)
    {
        if (candidate.LastKnownPosition is null || candidate.LastSeen is null)
            return Task.FromResult<CorrelationScore?>(null);

        var timeWindowSeconds = (DateTimeOffset.UtcNow - candidate.LastSeen.Value).TotalSeconds;
        if (timeWindowSeconds <= 0) timeWindowSeconds = 3600;

        var radiusMetres = Math.Max(1000, (candidate.LastSpeedMps ?? 5) * timeWindowSeconds * 1.2);

        // Rough degrees-to-metres conversion at mid-latitudes
        var distanceDegrees = position.Distance(candidate.LastKnownPosition);
        var distanceMetres = distanceDegrees * 111_320;

        if (distanceMetres <= radiusMetres)
        {
            var confidence = 0.3 + 0.4 * (1.0 - distanceMetres / radiusMetres);
            return Task.FromResult<CorrelationScore?>(
                new CorrelationScore(RuleId, confidence,
                    $"Within {distanceMetres:F0}m of candidate (radius {radiusMetres:F0}m)"));
        }

        return Task.FromResult<CorrelationScore?>(null);
    }
}
