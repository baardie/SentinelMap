using SentinelMap.Domain.Entities;

namespace SentinelMap.Domain.Interfaces;

public interface IEntityRepository
{
    Task<TrackedEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TrackedEntity> AddAsync(TrackedEntity entity, CancellationToken ct = default);
    Task UpdateAsync(TrackedEntity entity, CancellationToken ct = default);
}
