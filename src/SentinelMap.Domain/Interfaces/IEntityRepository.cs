using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;

namespace SentinelMap.Domain.Interfaces;

public interface IEntityRepository
{
    Task<TrackedEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TrackedEntity> AddAsync(TrackedEntity entity, CancellationToken ct = default);
    Task UpdateAsync(TrackedEntity entity, CancellationToken ct = default);
    Task UpdatePositionAsync(Guid entityId, Point position, double? speedMps, double? heading, DateTimeOffset lastSeen, CancellationToken ct = default);
}
