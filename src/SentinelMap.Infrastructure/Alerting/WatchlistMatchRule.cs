using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.SharedKernel.Enums;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Alerting;

public class WatchlistMatchRule : IAlertRule
{
    private readonly IWatchlistRepository _watchlistRepo;
    private readonly IDatabase _redisDb;

    public WatchlistMatchRule(IWatchlistRepository watchlistRepo, IDatabase redisDb)
    {
        _watchlistRepo = watchlistRepo;
        _redisDb = redisDb;
    }

    public string RuleId => "watchlist-match";
    public AlertType Type => AlertType.WatchlistMatch;

    public async Task<IReadOnlyList<AlertTrigger>> EvaluateAsync(TrackedEntity entity, CancellationToken ct = default)
    {
        var alertedKey = $"watchlist:alerted:{entity.Id}";

        // Debounce: if already alerted for this entity, skip
        var alreadyAlerted = await _redisDb.KeyExistsAsync(alertedKey);
        if (alreadyAlerted)
            return [];

        // Check all known identifiers against the watchlist
        bool isWatchlisted = false;

        // Check typed identifiers first
        foreach (var identifier in entity.Identifiers)
        {
            if (await _watchlistRepo.IsWatchlistedAsync(identifier.IdentifierType, identifier.IdentifierValue, ct))
            {
                isWatchlisted = true;
                break;
            }
        }

        // Fall back to DisplayName if no identifier matched
        if (!isWatchlisted && !string.IsNullOrEmpty(entity.DisplayName))
        {
            isWatchlisted = await _watchlistRepo.IsWatchlistedAsync("Name", entity.DisplayName, ct);
        }

        if (!isWatchlisted)
            return [];

        // Mark as alerted in Redis to debounce future evaluations
        await _redisDb.StringSetAsync(alertedKey, "1", TimeSpan.FromHours(24));

        return
        [
            new AlertTrigger(
                Type: AlertType.WatchlistMatch,
                Severity: AlertSeverity.Critical,
                Summary: $"Entity {entity.DisplayName} matched a watchlist entry",
                Details: $"Entity {entity.Id} ({entity.DisplayName}) is on the watchlist")
        ];
    }
}
