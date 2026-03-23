using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Infrastructure.Correlation;

/// <summary>
/// Checks if a candidate entity already has an identifier matching the observation's externalId.
/// </summary>
public class DirectIdMatchRule : ICorrelationRule
{
    public string RuleId => "DirectIdMatch";

    public Task<CorrelationScore?> EvaluateAsync(
        string sourceType,
        string externalId,
        string? displayName,
        Point position,
        TrackedEntity candidate,
        CancellationToken ct = default)
    {
        var match = candidate.Identifiers
            .Any(id => string.Equals(id.IdentifierValue, externalId, StringComparison.OrdinalIgnoreCase));

        if (match)
        {
            return Task.FromResult<CorrelationScore?>(
                new CorrelationScore(RuleId, 0.95, $"Identifier '{externalId}' matches existing entity"));
        }

        return Task.FromResult<CorrelationScore?>(null);
    }
}
