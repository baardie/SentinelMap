using Microsoft.EntityFrameworkCore;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Data;

namespace SentinelMap.Infrastructure.Repositories;

public class ObservationRepository : IObservationRepository
{
    private readonly SystemDbContext _db;

    public ObservationRepository(SystemDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Observation observation, CancellationToken ct = default)
    {
        _db.Observations.Add(observation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Observation?> GetByIdAsync(long id, DateTimeOffset observedAt, CancellationToken ct = default)
    {
        return await _db.Observations
            .Where(o => o.Id == id && o.ObservedAt == observedAt)
            .FirstOrDefaultAsync(ct);
    }
}
