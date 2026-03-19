using SentinelMap.Domain.Interfaces;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Pipeline;

public class RedisDeduplicationService : IDeduplicationService
{
    private readonly IDatabase _db;

    public RedisDeduplicationService(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    public async Task<bool> IsDuplicateAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var wasNew = await _db.StringSetAsync(key, 1, ttl, When.NotExists);
        return !wasNew;
    }

    public static string BuildKey(string sourceType, string externalId, double lat, double lon, DateTimeOffset timestamp)
    {
        // Convert to decimal before truncating to 4dp to avoid IEEE 754 imprecision
        // (e.g., 51.1234 * 10000 in double = 511233.999..., causing off-by-one in truncation).
        // Truncation toward zero ensures jitter only in the 5th+ decimal maps to the same bucket.
        var lat4 = (Math.Truncate((decimal)lat * 10000m) / 10000m).ToString("F4");
        var lon4 = (Math.Truncate((decimal)lon * 10000m) / 10000m).ToString("F4");
        var bucket = timestamp.ToUnixTimeSeconds() / 60;
        return $"dedup:{sourceType}:{externalId}:{lat4}:{lon4}:{bucket}";
    }
}
