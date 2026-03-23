using SentinelMap.Domain.Entities;

namespace SentinelMap.Domain.Interfaces;

public interface IWatchlistRepository
{
    Task<Watchlist> AddAsync(Watchlist watchlist, CancellationToken ct = default);
    Task<Watchlist?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Watchlist>> GetAllAsync(CancellationToken ct = default);
    Task AddEntryAsync(WatchlistEntry entry, CancellationToken ct = default);
    Task RemoveEntryAsync(Guid entryId, CancellationToken ct = default);
    Task<bool> IsWatchlistedAsync(string identifierType, string identifierValue, CancellationToken ct = default);
}
