using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Correlation;

/// <summary>
/// Normalises both names and computes Jaro-Winkler similarity.
/// Score >= 0.75 yields a scaled confidence; below threshold returns null.
/// </summary>
public class NameFuzzyMatchRule : ICorrelationRule
{
    private const double Threshold = 0.75;
    private const double ScalingFactor = 0.85;

    public string RuleId => "NameFuzzyMatch";

    public Task<CorrelationScore?> EvaluateAsync(
        string sourceType,
        string externalId,
        string? displayName,
        Point position,
        TrackedEntity candidate,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(candidate.DisplayName))
            return Task.FromResult<CorrelationScore?>(null);

        var normObservation = NameNormaliser.Normalise(displayName);
        var normCandidate = NameNormaliser.Normalise(candidate.DisplayName);

        if (string.IsNullOrEmpty(normObservation) || string.IsNullOrEmpty(normCandidate))
            return Task.FromResult<CorrelationScore?>(null);

        var similarity = JaroWinkler.Similarity(normObservation, normCandidate);

        if (similarity >= Threshold)
        {
            var confidence = similarity * ScalingFactor;
            return Task.FromResult<CorrelationScore?>(
                new CorrelationScore(RuleId, confidence,
                    $"Name similarity {similarity:F3} between '{displayName}' and '{candidate.DisplayName}'"));
        }

        return Task.FromResult<CorrelationScore?>(null);
    }
}
