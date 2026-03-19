using SentinelMap.Domain.Entities;

namespace SentinelMap.Domain.Interfaces;

public interface IObservationRepository
{
    Task AddAsync(Observation observation, CancellationToken ct = default);
    Task<Observation?> GetByIdAsync(long id, DateTimeOffset observedAt, CancellationToken ct = default);
}
