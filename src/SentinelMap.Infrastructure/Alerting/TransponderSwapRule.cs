using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.SharedKernel.Enums;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Alerting;

/// <summary>
/// Detects impossible position jumps — same entity appears at two locations
/// that are physically impossible given elapsed time (transponder swap / ID spoofing).
/// </summary>
public class TransponderSwapRule : IAlertRule
{
    // 1 degree of latitude ≈ 111 km
    private const double DegreesToMeters = 111_000.0;

    // Minimum distance threshold to avoid false positives from GPS noise
    private const double MinDistanceMeters = 50_000.0; // 50 km

    // Safety factor applied to the max-speed calculation
    private const double SafetyFactor = 1.5;

    // Speed cap used when entity has no known speed (slow vessel fallback)
    private const double DefaultFallbackSpeedMps = 15.0; // ~29 knots

    private readonly IDatabase _redis;

    public TransponderSwapRule(IDatabase redis)
    {
        _redis = redis;
    }

    public string RuleId => "transponder-swap";
    public AlertType Type => AlertType.TransponderSwap;

    public async Task<IReadOnlyList<AlertTrigger>> EvaluateAsync(
        TrackedEntity entity,
        CancellationToken ct = default)
    {
        if (entity.LastKnownPosition is null || entity.LastSeen is null)
            return [];

        var posKey = $"transponder:lastpos:{entity.Id}";

        // Read previous snapshot from Redis
        var cached = await _redis.StringGetAsync(posKey);

        // Snapshot current position for next evaluation
        var snapshot = $"{entity.LastKnownPosition.X:F6},{entity.LastKnownPosition.Y:F6},{entity.LastSeen.Value.ToUnixTimeSeconds()},{entity.LastSpeedMps ?? DefaultFallbackSpeedMps}";
        await _redis.StringSetAsync(posKey, snapshot, TimeSpan.FromHours(24));

        if (cached.IsNull)
            return []; // No previous position to compare against

        // Parse previous snapshot: lon,lat,unixSeconds,speedMps
        var parts = cached.ToString().Split(',');
        if (parts.Length < 4) return [];
        if (!double.TryParse(parts[0], out var prevLon)) return [];
        if (!double.TryParse(parts[1], out var prevLat)) return [];
        if (!long.TryParse(parts[2], out var prevUnix)) return [];
        if (!double.TryParse(parts[3], out var prevSpeedMps)) return [];

        var prevTime = DateTimeOffset.FromUnixTimeSeconds(prevUnix);
        var elapsedSeconds = (entity.LastSeen.Value - prevTime).TotalSeconds;

        // Skip if timestamps are going backwards or no time has elapsed
        if (elapsedSeconds <= 0) return [];

        // Calculate great-circle distance (Haversine approximation good enough for this range)
        var dLat = (entity.LastKnownPosition.Y - prevLat) * DegreesToMeters;
        var dLon = (entity.LastKnownPosition.X - prevLon) * DegreesToMeters
                   * Math.Cos(prevLat * Math.PI / 180.0);
        var distanceMeters = Math.Sqrt(dLat * dLat + dLon * dLon);

        // Below minimum threshold — likely GPS noise, not a swap
        if (distanceMeters < MinDistanceMeters)
            return [];

        // Maximum plausible distance given elapsed time and last known speed
        var speedMps = Math.Max(entity.LastSpeedMps ?? prevSpeedMps, DefaultFallbackSpeedMps);
        var maxPossibleMeters = speedMps * elapsedSeconds * SafetyFactor;

        if (distanceMeters <= maxPossibleMeters)
            return [];

        var distanceKm = Math.Round(distanceMeters / 1000.0, 1);
        var maxKm = Math.Round(maxPossibleMeters / 1000.0, 1);
        var elapsedMin = Math.Round(elapsedSeconds / 60.0, 1);

        var trigger = new AlertTrigger(
            Type: AlertType.TransponderSwap,
            Severity: AlertSeverity.High,
            Summary: $"Possible transponder swap detected for {entity.DisplayName}: jumped {distanceKm} km in {elapsedMin} min (max plausible: {maxKm} km)",
            Details: $"Entity {entity.Id} moved {distanceMeters:F0} m in {elapsedSeconds:F0} s; max plausible distance at speed {speedMps:F1} m/s with {SafetyFactor}x safety factor = {maxPossibleMeters:F0} m");

        return [trigger];
    }
}
