using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.SharedKernel.Enums;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Alerting;

public class RouteDeviationRule : IAlertRule
{
    private readonly IDatabase _redisDb;

    public RouteDeviationRule(IDatabase redisDb)
    {
        _redisDb = redisDb;
    }

    public string RuleId => "route-deviation";
    public AlertType Type => AlertType.RouteDeviation;

    public async Task<IReadOnlyList<AlertTrigger>> EvaluateAsync(TrackedEntity entity, CancellationToken ct = default)
    {
        if (entity.LastHeading is null || entity.LastSpeedMps is null || entity.LastSpeedMps < 1.0)
            return [];

        var key = $"route:headings:{entity.Id}";
        var debounceKey = $"route:deviation:debounce:{entity.Id}";

        // Check debounce (1 alert per entity per hour)
        if (await _redisDb.KeyExistsAsync(debounceKey))
            return [];

        // Get rolling headings
        var headingsStr = await _redisDb.ListRangeAsync(key, 0, 29); // last 30 headings

        // Add current heading
        await _redisDb.ListRightPushAsync(key, entity.LastHeading.Value.ToString("F1"));
        await _redisDb.ListTrimAsync(key, -30, -1); // keep last 30
        await _redisDb.KeyExpireAsync(key, TimeSpan.FromHours(2));

        if (headingsStr.Length < 20)
            return []; // Not enough baseline data

        // Calculate rolling average heading (circular mean)
        var headings = headingsStr.Select(h => double.Parse(h!)).ToList();
        var avgSin = headings.Average(h => Math.Sin(h * Math.PI / 180));
        var avgCos = headings.Average(h => Math.Cos(h * Math.PI / 180));
        var avgHeading = (Math.Atan2(avgSin, avgCos) * 180 / Math.PI + 360) % 360;

        // Calculate angular difference
        var diff = Math.Abs(entity.LastHeading.Value - avgHeading);
        if (diff > 180) diff = 360 - diff;

        if (diff < 60)
            return []; // Within normal deviation

        // Fire alert
        await _redisDb.StringSetAsync(debounceKey, "1", TimeSpan.FromHours(1));

        return [new AlertTrigger(
            Type: AlertType.RouteDeviation,
            Severity: AlertSeverity.Medium,
            Summary: $"{entity.DisplayName ?? "Entity"} deviated {diff:F0}\u00b0 from established route",
            Details: $"Current heading: {entity.LastHeading:F1}\u00b0, rolling average: {avgHeading:F1}\u00b0, deviation: {diff:F1}\u00b0"
        )];
    }
}
