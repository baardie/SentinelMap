using Microsoft.EntityFrameworkCore;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Data;

namespace SentinelMap.Infrastructure.Repositories;

public class AlertRepository : IAlertRepository
{
    private readonly SystemDbContext _db;

    public AlertRepository(SystemDbContext db)
    {
        _db = db;
    }

    public async Task<Alert> AddAsync(Alert alert, CancellationToken ct = default)
    {
        _db.Alerts.Add(alert);
        await _db.SaveChangesAsync(ct);
        return alert;
    }

    public async Task<Alert?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Alerts.FindAsync([id], ct);
    }

    public async Task<List<Alert>> GetFeedAsync(int limit = 50, CancellationToken ct = default)
    {
        return await _db.Alerts
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(Alert alert, CancellationToken ct = default)
    {
        _db.Alerts.Update(alert);
        await _db.SaveChangesAsync(ct);
    }
}
