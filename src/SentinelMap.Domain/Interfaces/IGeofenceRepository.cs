using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Domain.Interfaces;

public interface IGeofenceRepository
{
    Task<Geofence> AddAsync(Geofence geofence, CancellationToken ct = default);
    Task<Geofence?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Geofence>> GetAllActiveAsync(CancellationToken ct = default);
    Task UpdateAsync(Geofence geofence, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<List<Guid>> FindContainingAsync(Point position, CancellationToken ct = default);
}
