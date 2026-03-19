using SentinelMap.Domain.Interfaces;

namespace SentinelMap.Infrastructure.Pipeline;

/// <summary>Test double for IDeduplicationService. Not for production use.</summary>
public class InMemoryDeduplicationService : IDeduplicationService
{
    private readonly Dictionary<string, DateTimeOffset> _seen = new();

    public Task<bool> IsDuplicateAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (_seen.TryGetValue(key, out var expiry) && expiry > now)
            return Task.FromResult(true);

        _seen[key] = now.Add(ttl);
        return Task.FromResult(false);
    }
}
