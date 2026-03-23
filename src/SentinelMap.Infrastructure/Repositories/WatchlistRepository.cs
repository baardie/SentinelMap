using Microsoft.EntityFrameworkCore;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Data;

namespace SentinelMap.Infrastructure.Repositories;

public class WatchlistRepository : IWatchlistRepository
{
    private readonly SystemDbContext _db;

    public WatchlistRepository(SystemDbContext db)
    {
        _db = db;
    }

    public async Task<Watchlist> AddAsync(Watchlist watchlist, CancellationToken ct = default)
    {
        _db.Watchlists.Add(watchlist);
        await _db.SaveChangesAsync(ct);
        return watchlist;
    }

    public async Task<Watchlist?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Watchlists
            .Include(w => w.Entries)
            .FirstOrDefaultAsync(w => w.Id == id, ct);
    }

    public async Task<List<Watchlist>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Watchlists.ToListAsync(ct);
    }

    public async Task AddEntryAsync(WatchlistEntry entry, CancellationToken ct = default)
    {
        _db.WatchlistEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveEntryAsync(Guid entryId, CancellationToken ct = default)
    {
        var entry = await _db.WatchlistEntries.FindAsync([entryId], ct);
        if (entry is not null)
        {
            _db.WatchlistEntries.Remove(entry);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> IsWatchlistedAsync(string identifierType, string identifierValue, CancellationToken ct = default)
    {
        return await _db.WatchlistEntries
            .AnyAsync(e => e.IdentifierType == identifierType && e.IdentifierValue == identifierValue, ct);
    }
}
