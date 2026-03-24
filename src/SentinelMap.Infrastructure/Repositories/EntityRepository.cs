using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Data;
using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Infrastructure.Repositories;

public class EntityRepository : IEntityRepository
{
    private readonly SystemDbContext _db;

    public EntityRepository(SystemDbContext db)
    {
        _db = db;
    }

    public async Task<TrackedEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Entities
            .Include(e => e.Identifiers)
            .FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    public async Task<TrackedEntity> AddAsync(TrackedEntity entity, CancellationToken ct = default)
    {
        _db.Entities.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(TrackedEntity entity, CancellationToken ct = default)
    {
        _db.Entities.Update(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdatePositionAsync(
        Guid entityId,
        Point position,
        double? speedMps,
        double? heading,
        DateTimeOffset lastSeen,
        CancellationToken ct = default)
    {
        object?[] parameters = [position.X, position.Y, speedMps, heading, lastSeen, entityId];
        await _db.Database.ExecuteSqlRawAsync(
            @"UPDATE entities
              SET last_known_position = ST_SetSRID(ST_MakePoint({0}, {1}), 4326),
                  last_speed_mps = {2},
                  last_heading = {3},
                  last_seen = {4},
                  status = 'Active',
                  updated_at = now()
              WHERE id = {5}",
            parameters,
            ct);
    }

    public async Task<List<TrackedEntity>> FindStaleVesselsAsync(TimeSpan darkTimeout, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - darkTimeout;
        return await _db.Entities
            .Where(e => e.Type == EntityType.Vessel
                      && e.Status == EntityStatus.Active
                      && e.LastSeen != null
                      && e.LastSeen < cutoff)
            .ToListAsync(ct);
    }

    public async Task<List<TrackedEntity>> FindCandidatesAsync(Point position, double radiusMetres, TimeSpan seenSince, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - seenSince;
        return await _db.Entities
            .Include(e => e.Identifiers)
            .Where(e => e.LastSeen != null && e.LastSeen > cutoff
                      && e.LastKnownPosition != null
                      && e.LastKnownPosition.IsWithinDistance(position, radiusMetres / 111_320.0))
            .Take(50)
            .ToListAsync(ct);
    }
}
