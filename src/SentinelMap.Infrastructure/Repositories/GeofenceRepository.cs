using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.Infrastructure.Data;

namespace SentinelMap.Infrastructure.Repositories;

public class GeofenceRepository : IGeofenceRepository
{
    private readonly SystemDbContext _db;

    public GeofenceRepository(SystemDbContext db)
    {
        _db = db;
    }

    public async Task<Geofence> AddAsync(Geofence geofence, CancellationToken ct = default)
    {
        _db.Geofences.Add(geofence);
        await _db.SaveChangesAsync(ct);
        return geofence;
    }

    public async Task<Geofence?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Geofences.FindAsync([id], ct);
    }

    public async Task<List<Geofence>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _db.Geofences
            .Where(g => g.IsActive)
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(Geofence geofence, CancellationToken ct = default)
    {
        _db.Geofences.Update(geofence);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var geofence = await _db.Geofences.FindAsync([id], ct);
        if (geofence is not null)
        {
            geofence.IsActive = false;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<Guid>> FindContainingAsync(Point position, CancellationToken ct = default)
    {
        return await _db.Geofences
            .Where(g => g.IsActive && g.Geometry.Contains(position))
            .Select(g => g.Id)
            .ToListAsync(ct);
    }
}
