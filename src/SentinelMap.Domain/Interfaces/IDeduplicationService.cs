namespace SentinelMap.Domain.Interfaces;

/// <summary>
/// Returns true if the key was already seen within the TTL (duplicate).
/// Returns false if this is the first occurrence (and records it).
/// </summary>
public interface IDeduplicationService
{
    Task<bool> IsDuplicateAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
