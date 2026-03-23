using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Infrastructure.Alerting;

public class SpeedAnomalyRule : IAlertRule
{
    // Speed thresholds in m/s
    private const double VesselThresholdMps = 25.72;    // 50 knots
    private const double AircraftThresholdMps = 308.67; // 600 knots

    // Knots conversion: 1 m/s = 1.94384 knots
    private const double MpsToKnots = 1.94384;

    public string RuleId => "speed-anomaly";
    public AlertType Type => AlertType.SpeedAnomaly;

    public Task<IReadOnlyList<AlertTrigger>> EvaluateAsync(TrackedEntity entity, CancellationToken ct = default)
    {
        if (entity.LastSpeedMps is null)
            return Task.FromResult<IReadOnlyList<AlertTrigger>>([]);

        var speedMps = entity.LastSpeedMps.Value;

        double? thresholdMps = entity.Type switch
        {
            EntityType.Vessel => VesselThresholdMps,
            EntityType.Aircraft => AircraftThresholdMps,
            _ => null
        };

        // No threshold defined for this entity type
        if (thresholdMps is null)
            return Task.FromResult<IReadOnlyList<AlertTrigger>>([]);

        if (speedMps <= thresholdMps.Value)
            return Task.FromResult<IReadOnlyList<AlertTrigger>>([]);

        var speedKnots = Math.Round(speedMps * MpsToKnots, 1);
        var limitKnots = entity.Type == EntityType.Vessel ? 50 : 600;
        var entityKind = entity.Type == EntityType.Vessel ? "Vessel" : "Aircraft";

        var trigger = new AlertTrigger(
            Type: AlertType.SpeedAnomaly,
            Severity: AlertSeverity.Medium,
            Summary: $"{entityKind} {entity.DisplayName} exceeding speed limit: {speedKnots} knots (limit: {limitKnots} knots)",
            Details: $"Entity {entity.Id} speed {speedMps:F2} m/s exceeds threshold {thresholdMps.Value:F2} m/s");

        return Task.FromResult<IReadOnlyList<AlertTrigger>>([trigger]);
    }
}
